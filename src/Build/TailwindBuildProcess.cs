using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
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

    private readonly Dictionary<string, string?> _configsToPackageJsons = [];

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
    public async Task InitializeAsync(bool newProjectOpened = false)
    {
        if (newProjectOpened)
        {
            _initialized = false;

            EndAllProcesses();
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
        base.Dispose();
        VS.Events.BuildEvents.ProjectBuildStarted -= OnBuild;
        VS.Events.DocumentEvents.Saved -= OnFileSave;
        General.Saved -= OnGeneralSettingsChanged;

        foreach (var process in _outputFileToProcesses.Values)
        {
            process?.Dispose();
        }
        _outputFileToProcesses.Clear();
        _processToIsMinify.Clear();
        _secondaryProcess?.Dispose();

        _secondaryProcess = null;
    }

    /// <summary>
    /// Determines whether any tracked Tailwind or npm script process is active.
    /// </summary>
    /// <returns><see langword="true"/> if a process is active and building is enabled; otherwise, <see langword="false"/>.</returns>
    internal bool AreProcessesActive()
    {
        return _outputFileToProcesses.Values.Any(IsProcessActive)
            || IsProcessActive(_secondaryProcess);
    }

    /// <summary>
    /// Determines whether a process is running while Tailwind building is enabled.
    /// </summary>
    /// <param name="process">The process to inspect, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the process is running and building is enabled; otherwise, <see langword="false"/>.</returns>
    protected override bool IsProcessActive(Process? process)
    {
        if (AreSettingsInitialized && _settings.BuildType == BuildProcessOptions.None)
        {
            return false;
        }

        return base.IsProcessActive(process);
    }

    /// <summary>
    /// Updates CLI settings and build file mappings, restarting builds if processes were previously active.
    /// </summary>
    /// <param name="settings">The current extension settings.</param>
    /// <returns>A task that completes when settings and any required build restarts have been applied.</returns>
    protected override async Task SettingsChangedAsync(TailwindSettings settings)
    {
        var processActive = AreProcessesActive();
        await base.SettingsChangedAsync(settings);
        await SetFilePathsAsync(settings);

        // Restart process since SetFilePathsAsync stops the process
        if (processActive)
        {
            await BuildAllAsync(BuildBehavior.Default);
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
            var keys = _configsToPackageJsons
                .Keys.Where(f => f.Equals(filePath, StringComparison.InvariantCultureIgnoreCase))
                .ToList();

            foreach (var key in keys)
            {
                _configsToPackageJsons.Remove(key);
            }

            if (AreProcessesActive())
            {
                EndAllProcesses();
                ThreadHelper
                    .JoinableTaskFactory.RunAsync(() => BuildAllAsync(BuildBehavior.Default))
                    .FileAndForget(
                        nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/OnFileSave"
                    );
            }
        }

        var extension = Path.GetExtension(filePath);
        if (
            _settings.BuildType == BuildProcessOptions.OnSave
            && _settings.OnSaveTriggerFileExtensions.Contains(extension)
        )
        {
            ThreadHelper
                .JoinableTaskFactory.RunAsync(async () =>
                {
                    await WriteToBuildPaneAsync("Tailwind CSS: Building...");
                    await BuildAllAsync(BuildBehavior.Default);
                })
                .FileAndForget(
                    nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/OnFileSave"
                );
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
        if (
            _settings.BuildType != BuildProcessOptions.Manual
            && _settings.BuildType != BuildProcessOptions.ManualJIT
        )
        {
            ThreadHelper
                .JoinableTaskFactory.RunAsync(() => BuildAllAsync(BuildBehavior.Default))
                .FileAndForget(nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/OnBuild");
        }
    }

    /// <summary>
    /// Starts or submits builds for all configured input/output pairs while holding the build gate.
    /// </summary>
    /// <param name="buildBehavior">The requested minification behavior, or the default to use configured behavior.</param>
    /// <returns>A task that completes when build commands have been started or submitted, without waiting for watch processes to exit.</returns>
    internal async Task BuildAllAsync(BuildBehavior buildBehavior)
    {
        await _buildGate.WaitAsync();
        try
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
        finally
        {
            _buildGate.Release();
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
        if (!_configsToPackageJsons.TryGetValue(config.FilePath, out var packageJsonPath))
        {
            (hasScript, packageJsonPath) = await PackageJsonReader.ScriptExistsAsync(
                config.FilePath,
                _settings.BuildScript
            );
            _configsToPackageJsons[config.FilePath] = packageJsonPath;
        }
        else
        {
            hasScript = packageJsonPath is not null;
        }

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
                        () => RestartBuildProcess(process, input, output, minify)
                    );
                }
                else if (_settings.OverrideBuild)
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
                        _outputFileToProcesses.TryGetValue(output, out var process)
                        && IsProcessActive(process)
                    )
                    {
                        return;
                    }

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
                        () => RestartBuildProcess(process, input, output, minify)
                    );
                }
                else if (_settings.OverrideBuild)
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
    /// Starts or submits a single build while holding the shared build gate.
    /// </summary>
    /// <param name="input">The input CSS file path.</param>
    /// <param name="output">The output CSS file path.</param>
    /// <param name="minify">Whether to minify the generated CSS.</param>
    /// <returns>A task that completes when the build command has been started or submitted and the gate has been released.</returns>
    private async Task BuildOneWithGateAsync(string input, string output, bool minify)
    {
        await _buildGate.WaitAsync();
        try
        {
            await BuildOneAsync(input, output, minify);
        }
        finally
        {
            _buildGate.Release();
        }
    }

    /// <summary>
    /// Stops all tracked Tailwind and npm script processes and clears build process tracking.
    /// </summary>
    internal void EndAllProcesses()
    {
        var any = _outputFileToProcesses.Any();

        foreach (var process in _outputFileToProcesses.Values.ToList())
        {
            EndProcess(process);
        }

        _outputFileToProcesses.Clear();
        _processToIsMinify.Clear();

        if (any)
        {
            ThreadHelper.JoinableTaskFactory.Run(() =>
                WriteToBuildPaneAsync("Tailwind CSS: Build stopped")
            );
        }

        if (_secondaryProcess is not null)
        {
            EndProcess(_secondaryProcess);
        }
    }

    /// <summary>
    /// Stops a tracked build or npm script process and removes its build tracking information.
    /// </summary>
    /// <param name="process">The process to stop and remove.</param>
    internal void EndProcess(Process process)
    {
        if (IsProcessActive(process))
        {
            if (process == _secondaryProcess)
            {
                ThreadHelper.JoinableTaskFactory.Run(() =>
                    WriteToBuildPaneAsync(
                        $"Tailwind CSS: Build script '{_settings.BuildScript}' stopped"
                    )
                );
                _secondaryProcess = null;
            }

            StopProcess(process);
        }

        _outputFileToProcesses.Remove(
            _outputFileToProcesses
                .Where(p => p.Value == process)
                .Select(p => p.Key)
                .FirstOrDefault()
                ?? ""
        );
        _processToIsMinify.Remove(process);
    }

    /// <summary>
    /// Refreshes build file mappings and output paths, stopping existing processes when the mappings are rebuilt.
    /// </summary>
    /// <param name="settings">The settings that supply build pairs and default output names.</param>
    /// <returns>A task that completes when build file discovery and mapping updates are finished.</returns>
    private async Task SetFilePathsAsync(TailwindSettings settings)
    {
        _configsToPackageJsons.Clear();

        var buildFiles = settings.BuildFiles;

        if (buildFiles is null || buildFiles.Count == 0)
        {
            // Check the smallest css files first since tailwind css files (should) be small
            var cssFiles = (await Scanner.FileFinder.GetCssFilesAsync())
                .OrderBy(f => new FileInfo(f).Length)
                .ToList();

            if (cssFiles.Count == 0)
            {
                return;
            }

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
        else
        {
            buildFiles = buildFiles
                .Select(b => new BuildPair()
                {
                    Input = b.Input,
                    Output = b.Output,
                    Behavior = b.Behavior,
                })
                .ToList();
        }

        var buildFilesFiltered = buildFiles
            .Where(f => !string.IsNullOrEmpty(f.Input) && File.Exists(f.Input))
            .Select(f =>
            {
                f.Output = string.IsNullOrEmpty(f.Output)
                    ? Path.Combine(
                        PathHelpers.GetContainingDirectory(f.Input),
                        string.Format(
                            settings.DefaultOutputCssName.EndsWith(".css")
                                ? settings.DefaultOutputCssName
                                : settings.DefaultOutputCssName + ".css",
                            Path.GetFileNameWithoutExtension(f.Input)
                        )
                    )
                    : f.Output;
                return f;
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

        EndAllProcesses();
    }

    /// <summary>
    /// Writes an interop diagnostic message to the build output pane.
    /// </summary>
    /// <param name="message">The diagnostic message to display.</param>
    protected override void OnCliMessage(string message)
    {
        ThreadHelper.JoinableTaskFactory.Run(() => WriteToBuildPaneAsync(message));
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
    private void RestartBuildProcess(Process process, string input, string output, bool minify)
    {
        EndProcess(process);
        ThreadHelper
            .JoinableTaskFactory.RunAsync(() => BuildOneWithGateAsync(input, output, minify))
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/TailwindBuildProcess/Restart");
    }

    /// <summary>
    /// Interprets CLI or npm output, reports build status, and restarts builds after out-of-memory errors.
    /// </summary>
    /// <param name="sender">The process that produced the output.</param>
    /// <param name="data">The output line, without its line terminator.</param>
    protected override void OutputDataReceived(object sender, string data)
    {
        if (_settings.OverrideBuild || sender == _secondaryProcess)
        {
            if (
                sender is Process process
                && data.TrimEnd(' ', '>', '\\') == process.StartInfo.WorkingDirectory.TrimEnd('\\')
            )
            {
                ThreadHelper.JoinableTaskFactory.Run(() =>
                    WriteToBuildPaneAsync(
                        $"Tailwind CSS: Build script '{_settings.BuildScript}' finished"
                    )
                );
            }
            if (data.ToLower().Contains("error") || data.Contains("ERR!"))
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await LogErrorAsync("Tailwind CSS: " + data);
                });
            }
            // usually messages with backslashes (path) and > are the lines where the command is being written to the command prompt
            else if (
                !string.IsNullOrWhiteSpace(data)
                && !data.Contains("Microsoft")
                && (!data.Contains('>') || !data.Contains('\\'))
            )
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await VS.StatusBar.ShowMessageAsync($"Tailwind CSS: {data}");
                    await WriteToBuildPaneAsync($"Tailwind CSS: {data}");
                });
            }
        }
        else
        {
            // See https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/113
            // On large projects, Tailwind build process may run out memory; fix is to restart it
            if (data.Contains("JavaScript heap out of memory"))
            {
                if (sender is Process process && _outputFileToProcesses.ContainsValue(process))
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await WriteToBuildPaneAsync(
                            "Tailwind CSS: Build process ran out of memory; restarting..."
                        );
                    });

                    var output = _outputFileToProcesses.First(p => p.Value == process).Key;
                    var pair = _inputFileToBuildInfo.First(p => p.Value.Output == output);
                    var minify =
                        _processToIsMinify.TryGetValue(process, out var isMinify) && isMinify;

                    RestartBuildProcess(process, pair.Key, pair.Value.Output, minify);
                }
            }
            else if (data.Contains("Error"))
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await LogErrorAsync("Tailwind CSS: " + data);
                });
            }
            else if (data.Contains("Done in"))
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await LogSuccessAsync(data.Replace("Done in", "").Trim().Trim('.'));
                });
            }
            else if (_general.VerboseBuild)
            {
                if (!string.IsNullOrWhiteSpace(data) && !data.Contains("Microsoft"))
                {
                    // Regex: remove ANSI escape codes
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await WriteToBuildPaneAsync(
                            $"Tailwind CSS: {Regex.Replace(data.Trim(), @"\x1B\[[0-9;]*[mK]", "")}"
                        );
                    });
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
        _secondaryProcess = StartProcess(startInfo);

        _secondaryProcess.Exited += (_, _) =>
        {
            ThreadHelper.JoinableTaskFactory.Run(() =>
                WriteToBuildPaneAsync("Tailwind CSS: Build stopped")
            );
            _secondaryProcess?.Dispose();
            _secondaryProcess = null;
        };
    }

    /// <summary>
    /// Starts a Tailwind build process and registers tracking cleanup and status reporting on exit.
    /// </summary>
    /// <param name="startInfo">The Tailwind CLI shell launch settings.</param>
    /// <returns>The started process, which must be added to build tracking and configured for output reading.</returns>
    private Process CreateAndStartProcess(ProcessStartInfo startInfo)
    {
        var process = StartProcess(startInfo);

        process.Exited += (s, _) =>
        {
            var proc = (Process)s;
            _processToIsMinify.Remove(proc);

            proc.Dispose();
            string? keyToRemove = null;
            foreach (var kvp in _outputFileToProcesses)
            {
                if (kvp.Value == proc)
                {
                    keyToRemove = kvp.Key;
                    break;
                }
            }

            if (keyToRemove != null)
            {
                _outputFileToProcesses.Remove(keyToRemove);
            }

            if (_outputFileToProcesses.Count > 0)
            {
                ThreadHelper.JoinableTaskFactory.Run(() =>
                    WriteToBuildPaneAsync(
                        $"Tailwind CSS: Build stopped ({_outputFileToProcesses.Count} processes left)"
                    )
                );
            }
        };

        return process;
    }

    private class BuildSettings
    {
        public string Output { get; set; } = "";
        public BuildBehavior Behavior { get; set; } = BuildBehavior.Default;
    }
}
