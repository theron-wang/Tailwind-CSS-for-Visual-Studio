using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "VSTHRD102:Implement internal logic asynchronously",
    Justification = "Async methods called here are not expensive"
)]
internal sealed class TailwindBuildProcess : IDisposable
{
    [Import]
    internal ConfigFileScanner Scanner { get; set; } = null!;

    [Import]
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    [Import]
    internal PackageJsonReader PackageJsonReader { get; set; } = null!;

    [Import]
    internal ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;

    private Dictionary<string, BuildSettings> _inputFileToBuildInfo = [];

    private bool _initialized;
    private bool _subscribed;

    private bool _startingBuilds;
    private readonly SemaphoreSlim _buildGate = new(1, 1);

    // http://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/120
    // If we encounter a security error from powershell in running npx, use -ExecutionPolicy Unrestricted
    private bool _encounteredSecurityError;

    private readonly Dictionary<string, string?> _configsToPackageJsons = [];

    private readonly Dictionary<string, Process> _outputFileToProcesses = [];
    private readonly Dictionary<Process, bool> _processToIsMinify = [];
    private Process? _secondaryProcess;

    private TailwindSettings _settings = null!;
    private General _general = null!;

    private Guid _buildWindowGuid;

    /// <summary>
    /// Initializes the class; subscribes to events, sets up members
    /// </summary>
    /// <param name="newProjectOpened">A <see cref="bool"/> which should only be set to <see langword="true"/> in <see cref="TailwindCSSIntellisensePackage.InitializeAsync"/></param>
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

        var settings = await SettingsProvider.GetSettingsAsync();

        await SetFilePathsAsync(settings);

        // Prevent more than one subscription when projects are changed
        if (_subscribed == false)
        {
            VS.Events.BuildEvents.ProjectBuildStarted += OnBuild;
            VS.Events.DocumentEvents.Saved += OnFileSave;
            SettingsProvider.OnSettingsChanged += SettingsChangedAsync;
            General.Saved += OnGeneralSettingsChanged;
            _general = await General.GetLiveInstanceAsync();
            _subscribed = true;
        }
        _initialized = true;
    }

    public void Dispose()
    {
        VS.Events.BuildEvents.ProjectBuildStarted -= OnBuild;
        VS.Events.DocumentEvents.Saved -= OnFileSave;
        SettingsProvider.OnSettingsChanged -= SettingsChangedAsync;
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

    internal bool AreProcessesActive()
    {
        return _outputFileToProcesses.Values.Any(IsProcessActive)
            || IsProcessActive(_secondaryProcess);
    }

    /// <summary>
    /// A <see cref="bool"/> representing whether or not the build process is currently active
    /// </summary>
    private bool IsProcessActive(Process? process)
    {
        if (_settings?.BuildType == BuildProcessOptions.None)
        {
            return false;
        }
        else
        {
            return process != null && process.HasExited == false;
        }
    }

    /// <summary>
    /// Binds to <see cref="SettingsProvider.OnSettingsChanged"/> which updates the file paths and restarts the process if needed
    /// </summary>
    /// <param name="settings">The settings to pass on to <see cref="SetFilePathsAsync(TailwindSettings)"/></param>
    private async Task SettingsChangedAsync(TailwindSettings settings)
    {
        var processActive = AreProcessesActive();
        await SetFilePathsAsync(settings);

        // Restart process since SetFilePathsAsync stops the process
        if (processActive)
        {
            await BuildAllAsync(BuildBehavior.Default);
        }
    }

    private void OnGeneralSettingsChanged(General general)
    {
        _general = general;
    }

    /// <summary>
    /// Reloads if package.json has been modified + starts build process (OnBuild)
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget ok"
    )]
    private void OnFileSave(string filePath)
    {
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
    /// Starts the build process
    /// </summary>
    internal async Task BuildAllAsync(BuildBehavior buildBehavior)
    {
        await _buildGate.WaitAsync();
        try
        {
            if (
                _settings.ConfigurationFiles.Count == 0
                || _inputFileToBuildInfo == null
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
                            BuildBehavior.Default or _ => pair.Value.Behavior switch
                            {
                                BuildBehavior.Minified => true,
                                BuildBehavior.Unminified => false,
                                BuildBehavior.Default or _ => _settings.AutomaticallyMinify,
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

        var hasScript = false;
        string? packageJsonPath = null;

        if (!_configsToPackageJsons.ContainsKey(config.FilePath))
        {
            (hasScript, packageJsonPath) = await PackageJsonReader.ScriptExistsAsync(
                config.FilePath,
                _settings.BuildScript
            );
            _configsToPackageJsons[config.FilePath] = packageJsonPath;
        }
        else
        {
            packageJsonPath = _configsToPackageJsons[config.FilePath];
            hasScript = packageJsonPath is not null;
        }

        var needOtherProcess =
            _settings.OverrideBuild == false
            && hasScript
            && string.IsNullOrWhiteSpace(_settings.BuildScript) == false;
        // Run from the directory of the input file so Tailwind can find the configuration file, not us
        var dir = Path.GetDirectoryName(config.FilePath);

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
                    _settings.OverrideBuild == false
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
                    _settings.OverrideBuild == false
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

                    PostSetupProcess(process);

                    _outputFileToProcesses[output] = process;
                    _processToIsMinify[process] = minify;
                }
                else if (_settings.OverrideBuild)
                {
                    CreateAndSetAndStartSecondaryProcess(
                        GetProcessStartInfo(Path.GetDirectoryName(packageJsonPath))
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

                    var processInfo = GetProcessStartInfo(Path.GetDirectoryName(packageJsonPath));
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
                    _settings.OverrideBuild == false
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

                    ProcessStartInfo processInfo;

                    var watch =
                        _settings.BuildType == BuildProcessOptions.Default
                        || _settings.BuildType == BuildProcessOptions.ManualJIT;
                    // We need powershell to run --watch, otherwise the output file will not update.
                    // https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/111
                    if (watch)
                    {
                        processInfo = GetProcessStartInfo(dir, true);
                        // If we encountered a security error, use -ExecutionPolicy Unrestricted
                        processInfo.Arguments =
                            $"{(_encounteredSecurityError ? "-ExecutionPolicy Unrestricted" : "")} -Command ";
                    }
                    else
                    {
                        processInfo = GetProcessStartInfo(dir);
                        processInfo.Arguments = "/c ";
                    }

                    if (config.Version == TailwindVersion.V3)
                    {
                        processInfo.Arguments += GetCommand(
                            config,
                            $"-i \"{inputFile}\" -o \"{outputFile}\" -c \"{configFile}\" {(minify ? "--minify" : "")} {(watch ? "--watch" : "")}",
                            !watch,
                            watch
                        );
                    }
                    else
                    {
                        processInfo.Arguments += GetCommand(
                            config,
                            $"-i \"{inputFile}\" -o \"{outputFile}\" {(minify ? "--minify" : "")} {(watch ? "--watch" : "")}",
                            !watch,
                            watch
                        );
                    }

                    process = CreateAndStartProcess(processInfo);

                    OutputDataReceived(
                        process,
                        $"Build started in {processInfo.WorkingDirectory}: `{(watch ? "powershell" : "cmd")} {processInfo.Arguments}`"
                    );

                    PostSetupProcess(process);

                    _outputFileToProcesses[output] = process;
                    _processToIsMinify[process] = minify;
                }
                else if (_settings.OverrideBuild)
                {
                    var processInfo = GetProcessStartInfo(Path.GetDirectoryName(packageJsonPath));
                    processInfo.Arguments = $"/c npm run {_settings.BuildScript}";

                    CreateAndSetAndStartSecondaryProcess(processInfo);

                    PostSetupProcess(_secondaryProcess!);
                    await _secondaryProcess!.StandardInput.FlushAsync();
                    _secondaryProcess!.StandardInput.Close();
                }
            }

            #endregion
            #region Secondary process

            if (IsProcessActive(_secondaryProcess) == false && needOtherProcess)
            {
                await WriteToBuildPaneAsync(
                    $"Tailwind CSS: Running '{_settings.BuildScript}' script..."
                );

                var processInfo = GetProcessStartInfo(Path.GetDirectoryName(packageJsonPath));
                processInfo.Arguments = $"/c npm run {_settings.BuildScript}";

                CreateAndSetAndStartSecondaryProcess(processInfo);

                PostSetupProcess(_secondaryProcess!);

                await _secondaryProcess!.StandardInput.FlushAsync();
                _secondaryProcess!.StandardInput.Close();
            }

            #endregion
        }
    }

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
    /// Ends the build process
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
    /// Ends the build process
    /// </summary>
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

            // Tailwind --watch keeps building even with kill; use \x3 to say Ctrl+c to stop
            // https://stackoverflow.com/questions/283128/how-do-i-send-ctrlc-to-a-process-in-c
            process.StandardInput.WriteLine("\x3");
            process.StandardInput.Close();
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // The process may have already exited at this point, which is why this error could occur.
                // Do nothing with this exception since the desired result is already achieved.
            }
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

    private string GetCommand(
        ProjectCompletionValues project,
        string args,
        bool exit = false,
        bool powershell = false
    )
    {
        var suffix = exit ? " & exit" : "";
        if (string.IsNullOrWhiteSpace(_settings.TailwindCliPath) || !_settings.UseCli)
        {
            if (project.Version == TailwindVersion.V3)
            {
                return $"npx tailwindcss {args}{suffix}";
            }
            else
            {
                return $"npx @tailwindcss/cli {args}{suffix}";
            }
        }
        return powershell
            ? $"\"& '{_settings.TailwindCliPath}' {args.Replace('\"', '\'')}\"{suffix}"
            : $"\"\"{_settings.TailwindCliPath}\" {args}{suffix}\"";
    }

    /// <summary>
    /// Resets the file paths for the config file, output file, and input file
    /// </summary>
    /// <param name="settings">The settings to consume</param>
    private async Task SetFilePathsAsync(TailwindSettings settings)
    {
        _settings = settings;
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
                        Path.GetDirectoryName(f.Input),
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

    private void OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null)
        {
            return;
        }

        OutputDataReceived(sender, e.Data);
    }

    private void OutputDataReceived(object sender, string data)
    {
        if (data == null)
        {
            return;
        }

        if (data.Contains("PSSecurityException") && !_encounteredSecurityError)
        {
            _encounteredSecurityError = true;
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await WriteToBuildPaneAsync(
                    "Tailwind CSS: Encountered a PSSecurityException, trying again with -ExecutionPolicy Unrestricted: see https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/120"
                );
            });

            foreach (var process in _outputFileToProcesses.Values)
            {
                var output = _outputFileToProcesses.First(p => p.Value == process).Key;
                var pair = _inputFileToBuildInfo.First(p => p.Value.Output == output);
                var minify = _processToIsMinify.TryGetValue(process, out var isMinify) && isMinify;

                EndProcess(process);
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await BuildOneWithGateAsync(pair.Key, pair.Value.Output, minify);
                });
            }
        }

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
                string.IsNullOrWhiteSpace(data) == false
                && data.Contains("Microsoft") == false
                && (data.Contains('>') == false || data.Contains('\\') == false)
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

                    EndProcess(process);
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await BuildOneWithGateAsync(pair.Key, pair.Value.Output, minify);
                    });
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
                if (string.IsNullOrWhiteSpace(data) == false && data.Contains("Microsoft") == false)
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

    private async Task LogErrorAsync(string error)
    {
        await VS.StatusBar.ShowMessageAsync(
            "Tailwind CSS: An error occurred while building. Check the build output window for more details."
        );
        await WriteToBuildPaneAsync(error);
    }

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

    private static ProcessStartInfo GetProcessStartInfo(string dir, bool powershell = false)
    {
        return new ProcessStartInfo()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            FileName = powershell ? "powershell" : "cmd",
            WorkingDirectory = dir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
    }

    private void CreateAndSetAndStartSecondaryProcess(ProcessStartInfo startInfo)
    {
        _secondaryProcess = Process.Start(startInfo);
        _secondaryProcess.EnableRaisingEvents = true;

        _secondaryProcess.Exited += (s, e) =>
        {
            ThreadHelper.JoinableTaskFactory.Run(() =>
                WriteToBuildPaneAsync("Tailwind CSS: Build stopped")
            );
            _secondaryProcess?.Dispose();
            _secondaryProcess = null;
        };
    }

    private Process CreateAndStartProcess(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo);
        process.EnableRaisingEvents = true;

        process.Exited += (s, e) =>
        {
            var proc = (Process)s;
            _processToIsMinify.Remove(proc);

            proc?.Dispose();
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

    private void PostSetupProcess(Process process)
    {
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.OutputDataReceived += OutputDataReceived;
        process.ErrorDataReceived += OutputDataReceived;
    }

    private class BuildSettings
    {
        public string Output { get; set; } = "";
        public BuildBehavior Behavior { get; set; } = BuildBehavior.Default;
    }
}
