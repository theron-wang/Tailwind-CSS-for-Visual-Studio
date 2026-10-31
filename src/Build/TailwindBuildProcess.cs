using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Node;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Build;

/// <summary>
/// Provides methods to start, stop, and manage the process to build Tailwind
/// </summary>
[Export(typeof(TailwindBuildProcess))]
[PartCreationPolicy(CreationPolicy.Shared)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "VSTHRD102:Implement internal logic asynchronously",
    Justification = "Async methods called here are not expensive"
)]
internal class TailwindBuildProcess : TailwindCliInterop
{
    private readonly JoinableTaskCollection _buildTasks =
        ThreadHelper.JoinableTaskContext.CreateCollection();
    private readonly JoinableTaskFactory _buildTaskFactory;

    public TailwindBuildProcess()
    {
        _buildTaskFactory = ThreadHelper.JoinableTaskContext.CreateFactory(_buildTasks);
    }

    [Import]
    internal ConfigFileScanner Scanner { get; set; } = null!;

    [Import]
    internal PackageJsonReader PackageJsonReader { get; set; } = null!;

    [Import]
    internal ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;

    private Dictionary<string, BuildSettings> _inputFileToBuildInfo = [];

    private bool _initialized;
    private bool _subscribed;

    private bool _startingBuilds;
    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private bool _disposed;
    private Process[] _processSnapshot = [];
    private readonly ConditionalWeakTable<Process, BuildOutputContext> _outputContexts = new();

    private readonly Dictionary<string, (bool exists, string? path)> _configsToPackageJsons = new(
        StringComparer.OrdinalIgnoreCase
    );

    private readonly Dictionary<string, Process> _outputFileToProcesses = [];
    private readonly Dictionary<Process, bool> _processToIsMinify = [];
    private Process? _secondaryProcess;

    private General _general = null!;

    private Guid _buildWindowGuid;

    /// <summary>
    /// Loads settings and build file mappings, then subscribes to build, save, and general settings events.
    /// </summary>
    /// <param name="newProjectOpened">Whether to stop existing processes and repeat initialization for a newly opened project.</param>
    /// <returns>A task that completes when initialization is finished.</returns>
    /// <remarks>Set <paramref name="newProjectOpened"/> to <see langword="true"/> only from <see cref="TailwindCSSIntellisensePackage.InitializeAsync"/>.</remarks>
    public Task InitializeAsync(bool newProjectOpened = false) =>
        WithBuildGateAsync(() => InitializeCoreAsync(newProjectOpened));

    private async Task InitializeCoreAsync(bool newProjectOpened)
    {
        if (newProjectOpened)
        {
            _initialized = false;

            await EndAllProcessesCoreAsync();
        }

        if (_initialized)
        {
            return;
        }

        await InitializeSettingsAsync();
        await SetFilePathsAsync(_settings);

        // Prevent more than one subscription when projects are changed
        if (!_subscribed)
        {
            VS.Events.BuildEvents.ProjectBuildStarted += OnBuild;
            VS.Events.DocumentEvents.Saved += OnFileSave;
            General.Saved += OnGeneralSettingsChanged;
            _general = await General.GetLiveInstanceAsync();
            _subscribed = true;
        }
        _initialized = true;
    }

    /// <summary>
    /// Removes event subscriptions and disposes tracked build and npm script processes.
    /// </summary>
    public override void Dispose()
    {
        // Disposal may run on the UI thread while a build awaits a VS service.
        // Join its tasks so that synchronous shutdown can safely pump that work.
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            using var join = _buildTasks.Join();
            await WithBuildGateAsync(async () =>
            {
                base.Dispose();
                VS.Events.BuildEvents.ProjectBuildStarted -= OnBuild;
                VS.Events.DocumentEvents.Saved -= OnFileSave;
                General.Saved -= OnGeneralSettingsChanged;
                await EndAllProcessesCoreAsync();
                _disposed = true;
            });
        });
    }

    /// <summary>
    /// Determines whether any tracked Tailwind or npm script process is active.
    /// </summary>
    /// <returns><see langword="true"/> if a process is active and building is enabled; otherwise, <see langword="false"/>.</returns>
    internal bool AreProcessesActive()
    {
        return Volatile.Read(ref _processSnapshot).Any(IsProcessActive);
    }

    /// <summary>
    /// Serializes all build state changes, including callbacks from process event threads.
    /// </summary>
    private Task WithBuildGateAsync(Func<Task> action) =>
        _buildTaskFactory.RunAsync(() => WithBuildGateCoreAsync(action)).JoinAsync();

    private async Task WithBuildGateCoreAsync(Func<Task> action)
    {
        await _buildGate.WaitAsync();
        try
        {
            if (!_disposed)
            {
                await action();
            }
        }
        finally
        {
            // Menu status queries are synchronous: publish a snapshot rather than
            // making the UI thread wait on a build that may itself need the UI thread.
            Volatile.Write(
                ref _processSnapshot,
                _outputFileToProcesses
                    .Values.Concat(_secondaryProcess is null ? [] : new[] { _secondaryProcess })
                    .ToArray()
            );
            _buildGate.Release();
        }
    }

    /// <summary>
    /// Updates CLI settings and build file mappings, restarting builds if processes were previously active.
    /// </summary>
    /// <param name="settings">The current extension settings.</param>
    /// <returns>A task that completes when settings and any required build restarts have been applied.</returns>
    protected override Task SettingsChangedAsync(TailwindSettings settings)
    {
        return WithBuildGateAsync(() => ApplySettingsAsync(settings));
    }

    private async Task ApplySettingsAsync(TailwindSettings settings)
    {
        var processActive = AreProcessesActive();
        await base.SettingsChangedAsync(settings);
        await SetFilePathsAsync(settings);

        // Restart process since SetFilePathsAsync stops the process
        if (processActive)
        {
            await BuildAllCoreAsync(BuildBehavior.Default);
        }
    }

    /// <summary>
    /// Updates the general options used when reporting build output.
    /// </summary>
    /// <param name="general">The saved general options.</param>
    private void OnGeneralSettingsChanged(General general)
    {
        _general = general;
    }

    /// <summary>
    /// Handles saved files by refreshing build-script state or starting configured on-save builds.
    /// </summary>
    /// <param name="filePath">The path of the saved file.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget ok"
    )]
    private void OnFileSave(string filePath)
    {
        ThreadHelper
            .JoinableTaskFactory.RunAsync(() => WithBuildGateAsync(() => OnFileSaveAsync(filePath)))
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/OnFileSave");
    }

    private async Task OnFileSaveAsync(string filePath)
    {
        // Avoid doing any save-triggered work in solutions where Tailwind is not configured.
        if (_settings.ConfigurationFiles.Count == 0)
        {
            return;
        }

        if (
            Path.GetFileName(filePath)
                .Equals("package.json", StringComparison.InvariantCultureIgnoreCase)
            && !string.IsNullOrWhiteSpace(_settings.BuildScript)
        )
        {
            // Keys are configuration files, not package.json paths. Also discard
            // cached misses so adding the configured script takes effect on save.
            _configsToPackageJsons.Clear();

            if (AreProcessesActive())
            {
                await EndAllProcessesCoreAsync();
                await BuildAllCoreAsync(BuildBehavior.Default);
                return;
            }
        }

        var extension = Path.GetExtension(filePath);
        if (
            _settings.BuildType == BuildProcessOptions.OnSave
            && _settings.OnSaveTriggerFileExtensions.Any(e =>
                e.Trim().Equals(extension, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            await WriteToBuildPaneAsync("Tailwind CSS: Building...");
            await BuildAllCoreAsync(BuildBehavior.Default);
        }
    }

    /// <summary>
    /// Starts configured automatic Tailwind builds when a Visual Studio project build begins.
    /// </summary>
    /// <param name="project">The project whose build started, or <see langword="null"/> when invoked without a project.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget ok"
    )]
    private void OnBuild(Project? project = null)
    {
        ThreadHelper
            .JoinableTaskFactory.RunAsync(() =>
                WithBuildGateAsync(async () =>
                {
                    if (
                        _settings.BuildType != BuildProcessOptions.Manual
                        && _settings.BuildType != BuildProcessOptions.ManualJIT
                    )
                    {
                        await BuildAllCoreAsync(BuildBehavior.Default);
                    }
                })
            )
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/OnBuild");
    }

    /// <summary>
    /// Starts or submits builds for all configured input/output pairs while holding the build gate.
    /// </summary>
    /// <param name="buildBehavior">The requested minification behavior, or the default to use configured behavior.</param>
    /// <returns>A task that completes when build commands have been started or submitted, without waiting for watch processes to exit.</returns>
    internal Task BuildAllAsync(BuildBehavior buildBehavior) =>
        WithBuildGateAsync(() => BuildAllCoreAsync(buildBehavior));

    private async Task BuildAllCoreAsync(BuildBehavior buildBehavior)
    {
        if (
            _settings.ConfigurationFiles.Count == 0
            || _inputFileToBuildInfo.Count == 0
            || _settings.BuildType == BuildProcessOptions.None
        )
        {
            return;
        }

        _startingBuilds = true;

        try
        {
            foreach (var pair in _inputFileToBuildInfo)
            {
                await BuildOneAsync(
                    pair.Key,
                    pair.Value.Output,
                    buildBehavior switch
                    {
                        BuildBehavior.Minified => true,
                        BuildBehavior.Unminified => false,
                        _ => pair.Value.Behavior switch
                        {
                            BuildBehavior.Minified => true,
                            BuildBehavior.Unminified => false,
                            _ => _settings.AutomaticallyMinify,
                        },
                    }
                );
            }
        }
        finally
        {
            _startingBuilds = false;
        }
    }

    /// <summary>
    /// Runs or reuses Tailwind and optional npm script processes for a single input/output pair.
    /// </summary>
    /// <param name="input">The input CSS file path.</param>
    /// <param name="output">The output CSS file path.</param>
    /// <param name="minify">Whether to minify the generated CSS.</param>
    /// <returns>A task that completes when the required build commands have been started or submitted.</returns>
    private async Task BuildOneAsync(string input, string output, bool minify = false)
    {
        var config =
            await ProjectConfigurationManager.GetCompletionConfigurationByConfigFilePathAsync(
                input
            );
        config ??= await ProjectConfigurationManager.GetCompletionConfigurationByFilePathAsync(
            input
        );

        if (config is null)
        {
            return;
        }

        bool hasScript;
        if (!_configsToPackageJsons.TryGetValue(config.FilePath, out var script))
        {
            script = await PackageJsonReader.ScriptExistsAsync(
                config.FilePath,
                _settings.BuildScript
            );
            _configsToPackageJsons[config.FilePath] = script;
        }
        hasScript = script.exists;
        var packageJsonPath = script.path;

        var needOtherProcess =
            !_settings.OverrideBuild
            && hasScript
            && !string.IsNullOrWhiteSpace(_settings.BuildScript);
        // Run from the directory of the input file so Tailwind can find the configuration file, not us
        var dir = PathHelpers.GetContainingDirectory(config.FilePath);

        var inputFile = PathHelpers.GetRelativePath(input, dir)!;
        var outputFile = PathHelpers.GetRelativePath(output, dir)!;
        var configFile = Path.GetFileName(config.FilePath);

        if (_settings.BuildType == BuildProcessOptions.OnSave)
        {
            // Are all default processes active?
            #region Default process
            if (_settings.OverrideBuild && IsProcessActive(_secondaryProcess))
            {
                await _secondaryProcess!.StandardInput.WriteLineAsync(
                    $"npm run {_settings.BuildScript}"
                );
            }
            else if (
                _outputFileToProcesses.TryGetValue(output, out var process)
                && IsProcessActive(process)
            )
            {
                if (
                    !_settings.OverrideBuild
                    || !hasScript
                    || string.IsNullOrWhiteSpace(_settings.BuildScript)
                )
                {
                    if (config.Version == TailwindVersion.V3)
                    {
                        await process.StandardInput.WriteLineAsync(
                            GetCommand(
                                config,
                                $"-i \"{inputFile}\" -o \"{outputFile}\" -c \"{configFile}\" {(minify ? "--minify" : "")}"
                            )
                        );
                    }
                    else
                    {
                        await process.StandardInput.WriteLineAsync(
                            GetCommand(
                                config,
                                $"-i \"{inputFile}\" -o \"{outputFile}\" {(minify ? "--minify" : "")}"
                            )
                        );
                    }
                }
            }
            else
            {
                if (_outputFileToProcesses.Count == 0)
                {
                    await WriteToBuildPaneAsync("Tailwind CSS: Build started...");
                }
                else if (process is not null && !IsProcessActive(process))
                {
                    _outputFileToProcesses.Remove(output);
                    _processToIsMinify.Remove(process);
                }

                if (
                    !_settings.OverrideBuild
                    || !hasScript
                    || string.IsNullOrWhiteSpace(_settings.BuildScript)
                )
                {
                    process = CreateAndStartProcess(GetProcessStartInfo(dir));
                    _outputFileToProcesses[output] = process;
                    _processToIsMinify[process] = minify;

                    if (config.Version == TailwindVersion.V3)
                    {
                        await process.StandardInput.WriteLineAsync(
                            GetCommand(
                                config,
                                $"-i \"{inputFile}\" -o \"{outputFile}\" -c \"{configFile}\" {(minify ? "--minify" : "")}"
                            )
                        );
                    }
                    else
                    {
                        await process.StandardInput.WriteLineAsync(
                            GetCommand(
                                config,
                                $"-i \"{inputFile}\" -o \"{outputFile}\" {(minify ? "--minify" : "")}"
                            )
                        );
                    }

                    _outputFileToProcesses[output] = process;
                    _processToIsMinify[process] = minify;
                    PostSetupProcess(
                        process,
                        () => QueueBuildRestart(process, input, output, minify)
                    );
                }
                else if (_settings.OverrideBuild && !IsProcessActive(_secondaryProcess))
                {
                    CreateAndSetAndStartSecondaryProcess(
                        GetProcessStartInfo(
                            PathHelpers.GetContainingDirectory(
                                packageJsonPath
                                    ?? throw new InvalidOperationException(
                                        "The build script has no package.json path."
                                    )
                            )
                        )
                    );

                    await _secondaryProcess!.StandardInput.WriteLineAsync(
                        $"npm run {_settings.BuildScript}"
                    );

                    PostSetupProcess(_secondaryProcess);
                }
            }
            #endregion
            #region Secondary process
            if (needOtherProcess)
            {
                if (IsProcessActive(_secondaryProcess))
                {
                    await _secondaryProcess!.StandardInput.WriteLineAsync(
                        $"npm run {_settings.BuildScript}"
                    );
                }
                else
                {
                    await WriteToBuildPaneAsync(
                        $"Tailwind CSS: Running '{_settings.BuildScript}' script..."
                    );

                    var processInfo = GetProcessStartInfo(
                        PathHelpers.GetContainingDirectory(
                            packageJsonPath
                                ?? throw new InvalidOperationException(
                                    "The build script has no package.json path."
                                )
                        )
                    );
                    CreateAndSetAndStartSecondaryProcess(processInfo);

                    await _secondaryProcess!.StandardInput.WriteLineAsync(
                        $"npm run {_settings.BuildScript}"
                    );

                    PostSetupProcess(_secondaryProcess);
                }
            }
            #endregion
        }
        else
        {
            #region Default process

            if (
                _outputFileToProcesses.Count == 0
                || !_outputFileToProcesses.ContainsKey(output)
                || _startingBuilds
                || !_outputFileToProcesses.Values.All(IsProcessActive)
            )
            {
                if (_outputFileToProcesses.Count == 0)
                {
                    await WriteToBuildPaneAsync("Tailwind CSS: Build started...");
                }

                if (
                    !_settings.OverrideBuild
                    || !hasScript
                    || string.IsNullOrWhiteSpace(_settings.BuildScript)
                )
                {
                    if (
                        !_outputFileToProcesses.TryGetValue(output, out var process)
                        || !IsProcessActive(process)
                    )
                    {
                        var watch =
                            _settings.BuildType == BuildProcessOptions.Default
                            || _settings.BuildType == BuildProcessOptions.ManualJIT;
                        // We need powershell to run --watch, otherwise the output file will not update.
                        // https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/111
                        var processInfo = GetCliProcessStartInfo(
                            config,
                            $"-i \"{inputFile}\" -o \"{outputFile}\" {(config.Version == TailwindVersion.V3 ? $"-c \"{configFile}\"" : "")} {(minify ? "--minify" : "")} {(watch ? "--watch" : "")}",
                            powershell: watch
                        );

                        process = CreateAndStartProcess(processInfo);

                        OutputDataReceived(
                            process,
                            $"Build started in {processInfo.WorkingDirectory}: `{(watch ? "powershell" : "cmd")} {processInfo.Arguments}`"
                        );

                        _outputFileToProcesses[output] = process;
                        _processToIsMinify[process] = minify;
                        PostSetupProcess(
                            process,
                            () => QueueBuildRestart(process, input, output, minify)
                        );
                    }
                }
                else if (_settings.OverrideBuild && !IsProcessActive(_secondaryProcess))
                {
                    var processInfo = GetProcessStartInfo(
                        PathHelpers.GetContainingDirectory(
                            packageJsonPath
                                ?? throw new InvalidOperationException(
                                    "The build script has no package.json path."
                                )
                        )
                    );
                    processInfo.Arguments = $"/c npm run {_settings.BuildScript}";

                    CreateAndSetAndStartSecondaryProcess(processInfo);

                    PostSetupProcess(_secondaryProcess!);
                    await _secondaryProcess!.StandardInput.FlushAsync();
                    _secondaryProcess!.StandardInput.Close();
                }
            }

            #endregion
            #region Secondary process

            if (!IsProcessActive(_secondaryProcess) && needOtherProcess)
            {
                await WriteToBuildPaneAsync(
                    $"Tailwind CSS: Running '{_settings.BuildScript}' script..."
                );

                var processInfo = GetProcessStartInfo(
                    PathHelpers.GetContainingDirectory(
                        packageJsonPath
                            ?? throw new InvalidOperationException(
                                "The build script has no package.json path."
                            )
                    )
                );
                processInfo.Arguments = $"/c npm run {_settings.BuildScript}";

                CreateAndSetAndStartSecondaryProcess(processInfo);

                PostSetupProcess(_secondaryProcess!);

                await _secondaryProcess!.StandardInput.FlushAsync();
                _secondaryProcess!.StandardInput.Close();
            }

            #endregion
        }
    }

    /// <summary>
    /// Stops all tracked Tailwind and npm script processes and clears build process tracking.
    /// </summary>
    internal Task EndAllProcessesAsync() => WithBuildGateAsync(EndAllProcessesCoreAsync);

    private async Task EndAllProcessesCoreAsync()
    {
        var processes = _outputFileToProcesses
            .Values.Concat(_secondaryProcess is null ? [] : new[] { _secondaryProcess })
            .Distinct()
            .ToArray();

        foreach (var process in processes)
        {
            await EndProcessCoreAsync(process);
        }

        if (processes.Length > 0)
        {
            await WriteToBuildPaneAsync("Tailwind CSS: Build stopped");
        }
    }

    /// <summary>
    /// Stops a tracked build or npm script process and removes its build tracking information.
    /// </summary>
    /// <param name="process">The process to stop and remove.</param>
    private async Task EndProcessCoreAsync(Process process)
    {
        RemoveTrackedProcess(process);
        try
        {
            StopProcess(process);
            // Kill requests termination; wait off the UI thread before releasing
            // the handle so restarts/project changes do not race a still-exiting shell.
            if (IsProcessActive(process))
            {
                await Task.Run(() => process.WaitForExit(5000));
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private bool RemoveTrackedProcess(Process process)
    {
        var removed = false;
        if (ReferenceEquals(_secondaryProcess, process))
        {
            _secondaryProcess = null;
            removed = true;
        }
        foreach (
            var key in _outputFileToProcesses
                .Where(p => ReferenceEquals(p.Value, process))
                .Select(p => p.Key)
                .ToArray()
        )
        {
            _outputFileToProcesses.Remove(key);
            removed = true;
        }
        _processToIsMinify.Remove(process);
        return removed;
    }

    /// <summary>
    /// Refreshes build file mappings and output paths, stopping existing processes when the mappings are rebuilt.
    /// </summary>
    /// <param name="settings">The settings that supply build pairs and default output names.</param>
    /// <returns>A task that completes when build file discovery and mapping updates are finished.</returns>
    private async Task SetFilePathsAsync(TailwindSettings settings)
    {
        _configsToPackageJsons.Clear();

        var buildFiles = settings.BuildFiles.ToList();

        if (buildFiles.Count == 0)
        {
            // Check the smallest css files first since tailwind css files (should) be small
            var cssFiles = (await Scanner.FileFinder.GetCssFilesAsync())
                .OrderBy(f => new FileInfo(f).Length)
                .ToList();

            buildFiles = [];

            foreach (var file in cssFiles)
            {
                if (await IsFileTailwindCssAsync(file))
                {
                    buildFiles = [];
                    buildFiles.Add(new() { Input = file });
                    break;
                }
            }
        }
        var buildFilesFiltered = buildFiles
            .Where(f => !string.IsNullOrEmpty(f.Input) && File.Exists(f.Input))
            .Select(f =>
            {
                return f with
                {
                    Output = string.IsNullOrEmpty(f.Output)
                        ? Path.Combine(
                            PathHelpers.GetContainingDirectory(f.Input),
                            string.Format(
                                settings.DefaultOutputCssName.EndsWith(".css")
                                    ? settings.DefaultOutputCssName
                                    : settings.DefaultOutputCssName + ".css",
                                Path.GetFileNameWithoutExtension(f.Input)
                            )
                        )
                        : f.Output,
                };
            });

        _inputFileToBuildInfo = [];

        foreach (var pair in buildFilesFiltered)
        {
            _inputFileToBuildInfo[pair.Input] = new()
            {
                Behavior = pair.Behavior,
                Output = pair.Output,
            };
        }

        await EndAllProcessesCoreAsync();
    }

    /// <summary>
    /// Writes an interop diagnostic message to the build output pane.
    /// </summary>
    /// <param name="message">The diagnostic message to display.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "Process callbacks report output with FileAndForget"
    )]
    protected override void OnCliMessage(string message)
    {
        ThreadHelper
            .JoinableTaskFactory.RunAsync(() =>
                WithBuildGateAsync(() => WriteToBuildPaneAsync(message))
            )
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/Message");
    }

    /// <summary>
    /// Stops a build process and schedules its replacement through the build gate.
    /// </summary>
    /// <param name="process">The process to stop before restarting its build.</param>
    /// <param name="input">The input CSS file path.</param>
    /// <param name="output">The output CSS file path.</param>
    /// <param name="minify">Whether the replacement build should minify the generated CSS.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget ok"
    )]
    private void QueueBuildRestart(Process process, string input, string output, bool minify)
    {
        ThreadHelper
            .JoinableTaskFactory.RunAsync(() =>
                RestartBuildProcessAsync(process, input, output, minify)
            )
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/Restart");
    }

    private Task RestartBuildProcessAsync(
        Process process,
        string input,
        string output,
        bool minify
    ) =>
        WithBuildGateAsync(async () =>
        {
            // Exit and output callbacks from a replaced/stopped process must not
            // stop its replacement or bring a deliberately stopped build back.
            if (
                !_outputFileToProcesses.TryGetValue(output, out var current)
                || !ReferenceEquals(current, process)
            )
            {
                return;
            }
            await EndProcessCoreAsync(process);
            await BuildOneAsync(input, output, minify);
        });

    /// <summary>
    /// Interprets CLI or npm output, reports build status, and restarts builds after out-of-memory errors.
    /// </summary>
    /// <param name="sender">The process that produced the output.</param>
    /// <param name="data">The output line, without its line terminator.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "Process callbacks report output with FileAndForget"
    )]
    protected override void OutputDataReceived(object sender, string data)
    {
        if (
            Volatile.Read(ref _disposed)
            || sender is not Process process
            || !_outputContexts.TryGetValue(process, out var context)
        )
        {
            return;
        }

        ThreadHelper
            .JoinableTaskFactory.RunAsync(() => HandleOutputAsync(process, data, context))
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/Output");
    }

    private async Task HandleOutputAsync(Process sender, string data, BuildOutputContext context)
    {
        // Reporting can await the UI. Keep it outside the lifecycle gate so
        // verbose/slow output cannot hold up stopping or replacing a process.
        if (context.IsScript)
        {
            if (data.TrimEnd(' ', '>', '\\') == sender.StartInfo.WorkingDirectory.TrimEnd('\\'))
            {
                await WriteToBuildPaneAsync(
                    $"Tailwind CSS: Build script '{context.Settings.BuildScript}' finished"
                );
            }
            if (data.ToLower().Contains("error") || data.Contains("ERR!"))
            {
                await LogErrorAsync("Tailwind CSS: " + data);
            }
            // usually messages with backslashes (path) and > are the lines where the command is being written to the command prompt
            else if (
                !string.IsNullOrWhiteSpace(data)
                && !data.Contains("Microsoft")
                && (!data.Contains('>') || !data.Contains('\\'))
            )
            {
                await VS.StatusBar.ShowMessageAsync($"Tailwind CSS: {data}");
                await WriteToBuildPaneAsync($"Tailwind CSS: {data}");
            }
        }
        else
        {
            // See https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/113
            // On large projects, Tailwind build process may run out memory; fix is to restart it
            if (data.Contains("JavaScript heap out of memory"))
            {
                var restarting = false;
                await WithBuildGateAsync(() =>
                {
                    var output = _outputFileToProcesses
                        .FirstOrDefault(p => ReferenceEquals(p.Value, sender))
                        .Key;
                    var pair = _inputFileToBuildInfo.FirstOrDefault(p => p.Value.Output == output);
                    if (pair.Value is not null)
                    {
                        QueueBuildRestart(
                            sender,
                            pair.Key,
                            pair.Value.Output,
                            _processToIsMinify.TryGetValue(sender, out var minify) && minify
                        );
                        restarting = true;
                    }
                    return Task.CompletedTask;
                });
                if (restarting)
                {
                    await WriteToBuildPaneAsync(
                        "Tailwind CSS: Build process ran out of memory; restarting..."
                    );
                }
            }
            else if (data.Contains("Error"))
            {
                await LogErrorAsync("Tailwind CSS: " + data);
            }
            else if (data.Contains("Done in"))
            {
                await LogSuccessAsync(data.Replace("Done in", "").Trim().Trim('.'));
            }
            else if (_general.VerboseBuild)
            {
                if (!string.IsNullOrWhiteSpace(data) && !data.Contains("Microsoft"))
                {
                    // Regex: remove ANSI escape codes
                    await WriteToBuildPaneAsync(
                        $"Tailwind CSS: {Regex.Replace(data.Trim(), @"\x1B\[[0-9;]*[mK]", "")}"
                    );
                }
            }
        }
    }

    /// <summary>
    /// Displays a build failure notice in the status bar and writes the error to the build output pane.
    /// </summary>
    /// <param name="error">The error message to write.</param>
    /// <returns>A task that completes when the failure notice and error have been displayed.</returns>
    private async Task LogErrorAsync(string error)
    {
        await VS.StatusBar.ShowMessageAsync(
            "Tailwind CSS: An error occurred while building. Check the build output window for more details."
        );
        await WriteToBuildPaneAsync(error);
    }

    /// <summary>
    /// Reports a successful build with its elapsed duration and the current time.
    /// </summary>
    /// <param name="seconds">The elapsed duration reported by the CLI, possibly containing ANSI color codes.</param>
    /// <returns>A task that completes when the success message has been displayed in the status bar and build output pane.</returns>
    private async Task LogSuccessAsync(string seconds)
    {
        // Remove ANSI color codes
        seconds = Regex.Replace(seconds, @"\x1B\[[0-9;]*[mK]", "");

        await VS.StatusBar.ShowMessageAsync(
            $"Tailwind CSS: Build succeeded in {seconds} at {DateTime.Now.ToLongTimeString()}."
        );

        await WriteToBuildPaneAsync(
            $"Tailwind CSS: Build succeeded in {seconds} at {DateTime.Now.ToLongTimeString()}."
        );
    }

    /// <summary>
    /// Writes a message to the build output pane, creating a fallback pane when necessary.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <returns>A task that completes when the message has been written.</returns>
    private async Task WriteToBuildPaneAsync(string message)
    {
        var windowPane = await VS.Windows.GetOutputWindowPaneAsync(
            Community.VisualStudio.Toolkit.Windows.VSOutputWindowPane.Build
        );

        if (windowPane == null)
        {
            if (_buildWindowGuid != default)
            {
                windowPane = (await VS.Windows.GetOutputWindowPaneAsync(_buildWindowGuid))!;
            }
            else
            {
                windowPane = await VS.Windows.CreateOutputWindowPaneAsync("Build");
                _buildWindowGuid = windowPane.Guid;
            }
        }
        else
        {
            await windowPane.ActivateAsync();
        }

        await windowPane.WriteLineAsync(message);
    }

    /// <summary>
    /// Checks whether a CSS file contains a Tailwind directive or import.
    /// </summary>
    /// <param name="filePath">The CSS file path to read.</param>
    /// <returns>A task whose result is <see langword="true"/> if the file references Tailwind; otherwise, <see langword="false"/>.</returns>
    private async Task<bool> IsFileTailwindCssAsync(string filePath)
    {
        using (var fs = File.OpenRead(filePath))
        {
            using (var reader = new StreamReader(fs))
            {
                var text = await reader.ReadToEndAsync();

                if (
                    text.Contains("@tailwind")
                    || (text.Contains("@import") && text.Contains("tailwindcss"))
                )
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Starts and tracks the secondary npm script process, with output reporting and cleanup on exit.
    /// </summary>
    /// <param name="startInfo">The npm script shell launch settings.</param>
    private void CreateAndSetAndStartSecondaryProcess(ProcessStartInfo startInfo)
    {
        _secondaryProcess = CreateAndStartProcess(startInfo, isSecondary: true);
    }

    /// <summary>
    /// Starts a Tailwind build process and registers tracking cleanup and status reporting on exit.
    /// </summary>
    /// <param name="startInfo">The Tailwind CLI shell launch settings.</param>
    /// <returns>The started process, which must be added to build tracking and configured for output reading.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "Exit callbacks schedule cleanup with FileAndForget"
    )]
    private Process CreateAndStartProcess(ProcessStartInfo startInfo, bool isSecondary = false)
    {
        var process = StartProcess(startInfo);
        _outputContexts.Add(process, new(_settings, isSecondary));

        void OnExited(object? sender, EventArgs e) =>
            ThreadHelper
                .JoinableTaskFactory.RunAsync(() => CleanupExitedProcessAsync(process))
                .FileAndForget(nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/Exited");

        process.Exited += OnExited;
        // A short-lived command can finish before its handler is attached.
        if (process.HasExited)
        {
            OnExited(process, EventArgs.Empty);
        }

        return process;
    }

    private async Task CleanupExitedProcessAsync(Process process)
    {
        var tracked = false;
        // Wait for startup/PostSetupProcess to finish before draining the readers.
        await WithBuildGateAsync(() =>
        {
            tracked =
                ReferenceEquals(process, _secondaryProcess)
                || _outputFileToProcesses.ContainsValue(process);
            return Task.CompletedTask;
        });
        if (!tracked)
        {
            return;
        }

        try
        {
            await Task.Run(() => process.WaitForExit());
        }
        catch (InvalidOperationException)
        {
            // Stop/settings disposal can win while the exit callback drains output.
        }
        await WithBuildGateAsync(async () =>
        {
            if (!RemoveTrackedProcess(process))
            {
                return;
            }
            process.Dispose();
            await WriteToBuildPaneAsync(
                $"Tailwind CSS: Build stopped ({_outputFileToProcesses.Count} processes left)"
            );
        });
    }

    private sealed class BuildOutputContext(TailwindSettings settings, bool isScript)
    {
        internal TailwindSettings Settings { get; } = settings;
        internal bool IsScript { get; } = isScript;
    }

    private class BuildSettings
    {
        public string Output { get; set; } = "";
        public BuildBehavior Behavior { get; set; } = BuildBehavior.Default;
    }
}
