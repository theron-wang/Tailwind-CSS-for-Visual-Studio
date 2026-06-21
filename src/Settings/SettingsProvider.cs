using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;

namespace TailwindCSSIntellisense.Settings;

/// <summary>
/// A singleton class to provide settings and provides an event which is raised when settings are changed
/// </summary>
[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class SettingsProvider : IDisposable
{
    public SettingsProvider()
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            General.Saved += GeneralSettingsChanged;
            VS.Events.SolutionEvents.OnAfterOpenFolder += InvalidateCacheAndSettingsChanged;
            // Do it on solution open instead of project open so that if the wrong project
            // is loaded first (i.e., the project that does not contain tailwind.extension.json)
            // it doesn't mess up other parts of the extension up. This calls after the initial
            // load of all projects.
            VS.Events.SolutionEvents.OnAfterOpenSolution += InvalidateCacheAndSettingsChanged;
            VS.Events.DocumentEvents.Saved += OnFileSaved;
        });
    }

    [Import]
    public FileFinder FileFinder { get; set; } = null!;

    [Import]
    public ConfigFileScanner ConfigFileScanner { get; set; } = null!;

    [Import]
    public ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;

    private const string ExtensionConfigFileName = "tailwind.extension.json";

    private Task? _fileWritingTask;
    private TailwindSettings? _cachedSettings;

    private DateTime _lastInvalidateTime = DateTime.MinValue;
    private readonly TimeSpan _invalidationInterval = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _invalidationLock = new(1, 1);

    /// <summary>
    /// Event that is raised when the settings are changed.
    /// </summary>
    public Func<TailwindSettings, Task>? OnSettingsChanged;

    public void RefreshSettings()
    {
        _cachedSettings = null;
    }

    /// <summary>
    /// Retrieves extension settings asynchronously.
    /// </summary>
    /// <returns>The extension and project settings.</returns>
    public async Task<TailwindSettings> GetSettingsAsync()
    {
        TailwindSettings returnSettings;
        if (_cachedSettings is not null)
        {
            returnSettings = _cachedSettings;
        }
        else
        {
            var changed = false;
            var general = await General.GetLiveInstanceAsync();

            var activeProjectPath = await GetTailwindProjectDirectoryAsync();

            if (activeProjectPath == null)
            {
                return new TailwindSettings()
                {
                    EnableTailwindCss = general.UseTailwindCss,
                    DefaultOutputCssName = general.TailwindOutputFileName.Trim(),
                    OnSaveTriggerFileExtensions = general.TailwindOnSaveTriggerFileExtensions.Split(
                        ';'
                    ),
                    BuildType = general.BuildProcessType,
                    BuildScript = general.BuildScript,
                    OverrideBuild = general.OverrideBuild,
                    AutomaticallyMinify = general.AutomaticallyMinify,
                    TailwindCliPath = general.TailwindCliPath,
                    SortClassesType = general.ClassSortType,
                };
            }

            TailwindSettingsProjectOnly projectSettings;

            var path = Path.Combine(activeProjectPath, ExtensionConfigFileName);

            if (File.Exists(path))
            {
                if (_fileWritingTask != null)
                {
                    if (_fileWritingTask.IsCompleted)
                    {
                        _fileWritingTask = null;
                    }
                    else
                    {
                        await _fileWritingTask;
                    }
                }
                try
                {
                    using var fs = File.Open(
                        path,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.ReadWrite
                    );
                    projectSettings = (
                        await JsonSerializer.DeserializeAsync<TailwindSettingsProjectOnly>(fs)
                    )!;
                }
                catch (Exception ex)
                {
                    // Json file is malformed/empty

                    await VS.StatusBar.ShowMessageAsync(
                        "Tailwind CSS extension configuration file failed to load properly (check 'Extensions' output window for more details)"
                    );
                    await ex.LogAsync();

                    var file = await ConfigFileScanner.TryFindConfigurationFileAsync();

                    projectSettings = new TailwindSettingsProjectOnly()
                    {
                        ConfigurationFiles = file is null ? [] : [new() { Path = file }],
                    };

                    if (file != null)
                    {
                        changed = true;
                    }
                }
            }
            else
            {
                var file = await ConfigFileScanner.TryFindConfigurationFileAsync();

                projectSettings = new TailwindSettingsProjectOnly()
                {
                    ConfigurationFiles = file is null ? [] : [new() { Path = file }],
                };

                if (file != null)
                {
                    changed = true;
                }
            }

#pragma warning disable CS0612 // Type or member is obsolete
            // Backwards compatibility
            if (projectSettings.InputCssFile != null)
            {
                projectSettings.BuildFiles =
                [
                    new BuildPair()
                    {
#pragma warning disable CS8601 // Possible null reference assignment.
                        Input = projectSettings.InputCssFile,
                        Output = projectSettings.OutputCssFile
#pragma warning restore CS8601 // Possible null reference assignment.
                    },
                ];
                projectSettings.InputCssFile = null;
                projectSettings.OutputCssFile = null;
                changed = true;
            }

            if (projectSettings.ConfigurationFile != null)
            {
                projectSettings.ConfigurationFiles =
                [
                    new() { Path = projectSettings.ConfigurationFile },
                ];
                projectSettings.ConfigurationFile = null;
                changed = true;
            }
#pragma warning restore CS0612 // Type or member is obsolete

            var inputFiles = new HashSet<string>();

            if (projectSettings.BuildFiles is not null)
            {
                for (int i = 0; i < projectSettings.BuildFiles.Count; i++)
                {
                    var buildPair = projectSettings.BuildFiles[i];

#pragma warning disable CS8601 // Possible null reference assignment.
                    buildPair.Input = PathHelpers.GetAbsolutePath(
                        activeProjectPath,
                        buildPair.Input?.Trim()
                    );
#pragma warning restore CS8601 // Possible null reference assignment.
                    buildPair.Output =
                        PathHelpers.GetAbsolutePath(activeProjectPath, buildPair.Output?.Trim())
                        ?? "";
                    if (
                        buildPair.Input == null
                        || File.Exists(buildPair.Input) == false
                        || inputFiles.Contains(buildPair.Input)
                    )
                    {
                        projectSettings.BuildFiles.Remove(buildPair);
                        i--;
                        changed = true;
                        continue;
                    }

                    inputFiles.Add(buildPair.Input);
                }
            }

            if (projectSettings.ConfigurationFiles is not null)
            {
                for (int i = 0; i < projectSettings.ConfigurationFiles.Count; i++)
                {
                    var config = projectSettings.ConfigurationFiles[i];

#pragma warning disable CS8601 // Possible null reference assignment.
                    config.Path = PathHelpers.GetAbsolutePath(
                        activeProjectPath,
                        config.Path?.Trim()
                    );
#pragma warning restore CS8601 // Possible null reference assignment.

                    if (string.IsNullOrWhiteSpace(config.Path) || File.Exists(config.Path) == false)
                    {
                        projectSettings.ConfigurationFiles.Remove(config);
                        i--;
                        changed = true;
                    }
                    else if (Path.GetExtension(config.Path) == ".css")
                    {
                        projectSettings.ConfigurationFiles.Remove(config);
                        i--;

                        if (
                            projectSettings.BuildFiles is not null
                            && !projectSettings.BuildFiles.Any(b =>
                                b.Input.Equals(
                                    config.Path,
                                    StringComparison.InvariantCultureIgnoreCase
                                )
                            )
                        )
                        {
                            projectSettings.BuildFiles.Add(new() { Input = config.Path! });
                        }
                        changed = true;
                    }
                }
            }

            returnSettings = new TailwindSettings()
            {
                EnableTailwindCss = general.UseTailwindCss,
                DefaultOutputCssName = general.TailwindOutputFileName.Trim(),
                OnSaveTriggerFileExtensions = general.TailwindOnSaveTriggerFileExtensions.Split(
                    ';'
                ),
                BuildType = general.BuildProcessType,
                BuildScript = general.BuildScript,
                OverrideBuild = general.OverrideBuild,
                ConfigurationFiles = projectSettings.ConfigurationFiles ?? [],
                BuildFiles = projectSettings.BuildFiles ?? [],
                PackageConfigurationFile = PathHelpers.GetAbsolutePath(
                    activeProjectPath,
                    projectSettings?.PackageConfigurationFile?.Trim()
                ),
                AutomaticallyMinify = general.AutomaticallyMinify,
                TailwindCliPath = general.TailwindCliPath,
                UseCli = projectSettings!.UseCli,
                SortClassesType = general.ClassSortType,
                CustomRegexes = projectSettings.CustomRegexes,
            };

#pragma warning disable VSSDK007 // ThreadHelper.JoinableTaskFactory.RunAsync
            // Yes, this is horrible, but we need to call this as a fire and forget in case some caller of GetSettingsAsync must
            // be synchronous.
            ThreadHelper
                .JoinableTaskFactory.RunAsync(async () =>
                {
                    await ProjectConfigurationManager.OnSettingsChangedAsync(returnSettings);

                    if (changed)
                    {
                        await OverrideSettingsAsync(returnSettings);
                    }
                })
                .FileAndForget(
                    nameof(TailwindCSSIntellisense) + "/SettingsProvider/SettingsChanged"
                );
#pragma warning restore VSSDK007 // ThreadHelper.JoinableTaskFactory.RunAsync

            _cachedSettings = returnSettings;
        }

        // At this point, we've guaranteed that the settings have been generated at least once, so
        // we can proceed with setting ClassRegexHelper#GetTailwindSettings (since that is called only
        // in a synchronous context)
        ClassRegexHelper.GetTailwindSettings ??= GetSettingsAsync;

        return returnSettings;
    }

    /// <summary>
    /// Overrides the TailwindCSSIntellisense settings asynchronously.
    /// </summary>
    /// <param name="settings">The settings to override with.</param>
    /// <param name="preferredSaveDirectory">The preferred save directory of the file. Only use this if setting up Tailwind, so that
    /// the file is generated in the project that the user right-clicked.</param>
    public async Task OverrideSettingsAsync(
        TailwindSettings settings,
        string? preferredSaveDirectory = null
    )
    {
        // Prevents two tasks from writing to the same file at the same time
        if (_fileWritingTask != null)
        {
            if (_fileWritingTask.IsCompleted)
            {
                _fileWritingTask = null;
            }
            else
            {
                await _fileWritingTask;
            }
        }

        if (settings.ConfigurationFiles is not null)
        {
            for (int i = 0; i < settings.ConfigurationFiles.Count; i++)
            {
                var config = settings.ConfigurationFiles[i];

                if (string.IsNullOrWhiteSpace(config.Path) || File.Exists(config.Path) == false)
                {
                    settings.ConfigurationFiles.Remove(config);
                    i--;
                }
            }
        }

        for (int i = 0; i < settings.BuildFiles.Count; i++)
        {
            var buildPair = settings.BuildFiles[i];

            if (string.IsNullOrWhiteSpace(buildPair.Input) || File.Exists(buildPair.Input) == false)
            {
                settings.BuildFiles.Remove(buildPair);
                i--;
            }
        }

        var defaultConfigFile =
            settings.ConfigurationFiles.FirstOrDefault()?.Path
            ?? settings.ConfigurationFiles.FirstOrDefault()?.Path;

        string? oldDefaultConfigFile = null;
        if (_cachedSettings is not null)
        {
            oldDefaultConfigFile = _cachedSettings.ConfigurationFiles.FirstOrDefault()?.Path;
        }

        var projectRoot = preferredSaveDirectory ?? await GetTailwindProjectDirectoryAsync();

        if (projectRoot is null)
        {
            return;
        }

        var desiredProjectRoot = await GetDesiredConfigurationDirectoryAsync(defaultConfigFile);
        var copyBuildFilePair = new List<BuildPair>();

        foreach (var buildFilePair in settings.BuildFiles)
        {
            copyBuildFilePair.Add(
                new()
                {
                    Input = PathHelpers.GetRelativePath(buildFilePair.Input, projectRoot)!,
                    Output = string.IsNullOrWhiteSpace(buildFilePair.Output)
                        ? ""
                        : PathHelpers.GetRelativePath(buildFilePair.Output, projectRoot)!,
                }
            );
        }

        // Build list of config files not used as build inputs, with paths made relative to project root.
        List<ConfigurationFile> configurationFiles =
        [
            .. settings
                .ConfigurationFiles.Where(cf =>
                    settings.BuildFiles.All(b =>
                        !b.Input.Equals(cf.Path, StringComparison.InvariantCultureIgnoreCase)
                    )
                )
                .Select(cf =>
                {
                    return new ConfigurationFile()
                    {
                        Path = PathHelpers.GetRelativePath(cf.Path.Trim(), projectRoot)!,
                    };
                }),
        ];

        var projectSettings = new TailwindSettingsProjectOnly()
        {
            ConfigurationFiles = configurationFiles.Count > 0 ? configurationFiles : null,
            BuildFiles = copyBuildFilePair,
            PackageConfigurationFile = settings.PackageConfigurationFile is null
                ? null
                : PathHelpers.GetRelativePath(settings.PackageConfigurationFile, projectRoot),
            UseCli = settings.UseCli,
        };

        if (
            (
                projectSettings.ConfigurationFiles is null
                || projectSettings.ConfigurationFiles.Count == 0
            ) && (projectSettings.BuildFiles is null || projectSettings.BuildFiles.Count == 0)
        )
        {
            // Delete if empty, do not save if it doesn't exist yet

            if (File.Exists(Path.Combine(projectRoot, ExtensionConfigFileName)))
            {
                try
                {
                    File.Delete(Path.Combine(projectRoot, ExtensionConfigFileName));
                }
                catch { }
            }
        }
        else
        {
            // If the configuration file is not located in the same project as the configuration file,
            // move it there. If the configuration file has not been changed, however, respect the user's
            // decision to keep it where it is.
            if (
                preferredSaveDirectory is null
                && desiredProjectRoot != null
                && desiredProjectRoot != projectRoot
                && (
                    _cachedSettings is null
                    || (defaultConfigFile is not null && defaultConfigFile != oldDefaultConfigFile)
                )
            )
            {
                if (File.Exists(Path.Combine(projectRoot, ExtensionConfigFileName)))
                {
                    try
                    {
                        File.Delete(Path.Combine(projectRoot, ExtensionConfigFileName));
                    }
                    catch (Exception ex)
                    {
                        await ex.LogAsync(
                            $"Tailwind CSS: Failed to delete old configuration file at ${Path.Combine(projectRoot, ExtensionConfigFileName)}"
                        );
                    }
                }

                projectRoot = desiredProjectRoot;
            }

            using var fs = File.Open(
                Path.Combine(projectRoot, ExtensionConfigFileName),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            );
            _fileWritingTask = JsonSerializer.SerializeAsync(
                fs,
                projectSettings,
                options: new() { WriteIndented = true }
            );
            await _fileWritingTask;
        }

        _cachedSettings = settings;

        await ProjectConfigurationManager.OnSettingsChangedAsync(settings);

        if (OnSettingsChanged != null)
        {
            // Cannot call OnSettingsChanged.Invoke() because that only calls the last subscriber
            var tasks = OnSettingsChanged
                .GetInvocationList()
                .Cast<Func<TailwindSettings, Task>>()
                .Select(d => d(settings));

            await Task.WhenAll(tasks);
        }
    }

    public void Dispose()
    {
        General.Saved -= GeneralSettingsChanged;
        VS.Events.SolutionEvents.OnAfterOpenFolder -= InvalidateCacheAndSettingsChanged;
        VS.Events.SolutionEvents.OnAfterOpenSolution -= InvalidateCacheAndSettingsChanged;
        VS.Events.DocumentEvents.Saved -= OnFileSaved;
    }

    public async Task<string> GetFilePathAsync()
    {
        return Path.Combine(await GetTailwindProjectDirectoryAsync(), ExtensionConfigFileName);
    }

    private async Task<string?> GetDesiredConfigurationDirectoryAsync(string? configPath)
    {
        var projects = await VS.Solutions.GetAllProjectsAsync();

        if (projects.Any() == false || string.IsNullOrWhiteSpace(configPath))
        {
            return null;
        }

        // Try to find the project that contains the config file, and return
        // its path
        string? bestMatch = null;
        var numberOfSlashes = configPath!
            .Replace(Path.DirectorySeparatorChar, '/')
            .Count(c => c == '/');

        foreach (var p in projects)
        {
            var projectRoot = Path.GetDirectoryName(p.FullPath);

            var relativePath = PathHelpers.GetRelativePath(configPath, projectRoot)!;

            if (!configPath.StartsWith(".."))
            {
                var numSlashes = relativePath.Count(c => c == Path.DirectorySeparatorChar);

                if (numSlashes < numberOfSlashes)
                {
                    bestMatch = projectRoot;
                    numberOfSlashes = numSlashes;
                }
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Finds the directory of the tailwind.extension.json file.
    /// </summary>
    private async Task<string?> GetTailwindProjectDirectoryAsync()
    {
        var projects = await VS.Solutions.GetAllProjectsAsync(ProjectStateFilter.All);

        var solution = await VS.Solutions.GetCurrentSolutionAsync();

        if (projects == null || projects.Any() == false)
        {
            var miscProjectPath = await FileFinder.GetCurrentMiscellaneousProjectPathAsync();

            return miscProjectPath;
        }
        else
        {
            foreach (var p in projects)
            {
                if (
                    File.Exists(
                        Path.Combine(Path.GetDirectoryName(p.FullPath), ExtensionConfigFileName)
                    )
                )
                {
                    return Path.GetDirectoryName(p.FullPath);
                }
            }

            return Path.GetDirectoryName(
                projects
                    .FirstOrDefault(p =>
                        DefaultConfigurationFileNames.Names.Any(n =>
                            File.Exists(Path.Combine(Path.GetDirectoryName(p.FullPath), n))
                        )
                    )
                    ?.FullPath
                    ?? projects.First().FullPath
            );
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget ok"
    )]
    private void GeneralSettingsChanged(General settings)
    {
        ThreadHelper
            .JoinableTaskFactory.RunAsync(() => GeneralSettingsChangedAsync(settings))
            .FileAndForget(
                nameof(TailwindCSSIntellisense) + "/SettingsProvider/GeneralSettingsChanged"
            );
    }

    private async Task GeneralSettingsChangedAsync(General settings)
    {
        var origSettings = await GetSettingsAsync();

        if (
            settings.UseTailwindCss != origSettings.EnableTailwindCss
            || settings.TailwindOutputFileName != origSettings.DefaultOutputCssName
            || !settings
                .TailwindOnSaveTriggerFileExtensions.Split(
                    [';'],
                    StringSplitOptions.RemoveEmptyEntries
                )
                .Select(s => s.Trim())
                .SequenceEqual(
                    origSettings.OnSaveTriggerFileExtensions.Select(s => s.Trim()),
                    StringComparer.InvariantCultureIgnoreCase
                )
            || settings.BuildProcessType != origSettings.BuildType
            || settings.BuildScript != origSettings.BuildScript
            || settings.OverrideBuild != origSettings.OverrideBuild
            || settings.AutomaticallyMinify != origSettings.AutomaticallyMinify
            || settings.TailwindCliPath != origSettings.TailwindCliPath
            || settings.ClassSortType != origSettings.SortClassesType
        )
        {
            origSettings.EnableTailwindCss = settings.UseTailwindCss;
            origSettings.DefaultOutputCssName = settings.TailwindOutputFileName;
            origSettings.OnSaveTriggerFileExtensions =
                settings.TailwindOnSaveTriggerFileExtensions.Split(';');
            origSettings.BuildType = settings.BuildProcessType;
            origSettings.BuildScript = settings.BuildScript;
            origSettings.OverrideBuild = settings.OverrideBuild;
            origSettings.AutomaticallyMinify = settings.AutomaticallyMinify;
            origSettings.TailwindCliPath = settings.TailwindCliPath;
            origSettings.SortClassesType = settings.ClassSortType;

            if (OnSettingsChanged is not null)
            {
                // Cannot call OnSettingsChanged.Invoke() because that only calls the last subscriber
                var tasks = OnSettingsChanged
                    .GetInvocationList()
                    .Cast<Func<TailwindSettings, Task>>()
                    .Select(d => d(origSettings));

                await Task.WhenAll(tasks);
            }
        }

        _cachedSettings = origSettings;
    }

    private void InvalidateCacheAndSettingsChanged(string file)
    {
        InvalidateCacheAndSettingsChanged(solution: null);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "Caller must be synchronous but OnSettingsChanged could be expensive"
    )]
    private void InvalidateCacheAndSettingsChanged(Solution? solution)
    {
        ThreadHelper
            .JoinableTaskFactory.RunAsync(InvalidateCacheAndSettingsChangedImplAsync)
            .FileAndForget(
                nameof(TailwindCSSIntellisense)
                    + "/SettingsProvider/InvalidateCacheAndSettingsChanged"
            );
    }

    private async Task InvalidateCacheAndSettingsChangedImplAsync()
    {
        _cachedSettings = null;
        if (OnSettingsChanged == null || !await _invalidationLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            // Debounce
            var timeSinceLast = DateTime.UtcNow - _lastInvalidateTime;
            _lastInvalidateTime = DateTime.UtcNow;

            if (timeSinceLast < _invalidationInterval)
            {
                return;
            }

            var settings = await GetSettingsAsync();

            // Cannot call OnSettingsChanged.Invoke() because that only calls the last subscriber
            var tasks = OnSettingsChanged
                .GetInvocationList()
                .Cast<Func<TailwindSettings, Task>>()
                .Select(d => d(settings));

            await Task.WhenAll(tasks);
        }
        finally
        {
            _invalidationLock.Release();
        }
    }

    private void OnFileSaved(string file)
    {
        if (Path.GetFileName(file) == ExtensionConfigFileName)
        {
            InvalidateCacheAndSettingsChanged(file);
        }
    }
}
