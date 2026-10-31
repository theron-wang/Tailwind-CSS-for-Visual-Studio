using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

    public const string ExtensionConfigFileName = "tailwind.extension.json";
    private TailwindSettings? _cachedSettings;

    private readonly TimeSpan _invalidationInterval = TimeSpan.FromSeconds(5);

    private CancellationTokenSource _debounceCts = new();
    private readonly SemaphoreSlim _invocationLock = new(1, 1);
    private readonly SemaphoreSlim _extensionConfigWriteLock = new(1, 1);

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
                returnSettings = new TailwindSettings()
                {
                    EnableTailwindCss = general.UseTailwindCss,
                    DefaultOutputCssName = general.TailwindOutputFileName.Trim(),
                    OnSaveTriggerFileExtensions =
                    [
                        .. general.TailwindOnSaveTriggerFileExtensions.Split(';'),
                    ],
                    BuildType = general.BuildProcessType,
                    BuildScript = general.BuildScript,
                    OverrideBuild = general.OverrideBuild,
                    AutomaticallyMinify = general.AutomaticallyMinify,
                    TailwindCliPath = general.TailwindCliPath,
                    SortClassesType = general.ClassSortType,
                    CanonicalizeClassesType = general.ClassCanonicalizationType,
                };

                // Opening solutions still need uncached defaults until discovery is ready.
                // A settled empty workspace must discard the previous solution's configurations.
                if (!await VS.Solutions.IsOpeningAsync())
                {
                    _cachedSettings = returnSettings;
                    QueueSettingsChanged(returnSettings);
                }

                return returnSettings;
            }

            TailwindSettingsProjectOnly projectSettings;

            var path = Path.Combine(activeProjectPath, ExtensionConfigFileName);

            if (File.Exists(path))
            {
                try
                {
                    await _extensionConfigWriteLock.WaitAsync();
                    using var fs = File.Open(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite
                    );
                    projectSettings =
                        (await JsonSerializer.DeserializeAsync<TailwindSettingsProjectOnly>(fs))
                        ?? throw new JsonException("Expected an extension settings object.");
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
                finally
                {
                    _extensionConfigWriteLock.Release();
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
                        Input = projectSettings.InputCssFile,
                        Output = projectSettings.OutputCssFile ?? "",
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

                    buildPair = buildPair with
                    {
                        Input =
                            PathHelpers.GetAbsolutePath(activeProjectPath, buildPair.Input?.Trim())
                            ?? "",
                        Output =
                            PathHelpers.GetAbsolutePath(activeProjectPath, buildPair.Output?.Trim())
                            ?? "",
                    };
                    projectSettings.BuildFiles[i] = buildPair;
                    if (
                        buildPair.Input == null
                        || !File.Exists(buildPair.Input)
                        || !inputFiles.Add(buildPair.Input)
                    )
                    {
                        projectSettings.BuildFiles.Remove(buildPair);
                        i--;
                        changed = true;
                        continue;
                    }
                }
            }

            if (projectSettings.ConfigurationFiles is not null)
            {
                for (int i = 0; i < projectSettings.ConfigurationFiles.Count; i++)
                {
                    var config = projectSettings.ConfigurationFiles[i];

                    config = config with
                    {
                        Path =
                            PathHelpers.GetAbsolutePath(activeProjectPath, config.Path?.Trim())
                            ?? "",
                    };
                    projectSettings.ConfigurationFiles[i] = config;

                    if (string.IsNullOrWhiteSpace(config.Path) || !File.Exists(config.Path))
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
                OnSaveTriggerFileExtensions = general
                    .TailwindOnSaveTriggerFileExtensions.Split(';')
                    .ToImmutableList(),
                BuildType = general.BuildProcessType,
                BuildScript = general.BuildScript,
                OverrideBuild = general.OverrideBuild,
                ConfigurationFiles = projectSettings.ConfigurationFiles?.ToImmutableList() ?? [],
                BuildFiles = projectSettings.BuildFiles?.ToImmutableList() ?? [],
                PackageConfigurationFile = PathHelpers.GetAbsolutePath(
                    activeProjectPath,
                    projectSettings.PackageConfigurationFile?.Trim()
                ),
                AutomaticallyMinify = general.AutomaticallyMinify,
                TailwindCliPath = general.TailwindCliPath,
                UseCli = projectSettings!.UseCli,
                SortClassesType = general.ClassSortType,
                CanonicalizeClassesType = general.ClassCanonicalizationType,
                CustomRegexes = projectSettings.CustomRegexes,
            };

            // Consumers may copy these settings immediately. Resolve CSS inputs before
            // publishing them, independently of background completion snapshot loading.
            returnSettings = returnSettings with
            {
                ConfigurationFiles =
                    await ProjectConfigurationManager.ResolveConfigurationFilesAsync(
                        returnSettings
                    ),
            };
            _cachedSettings = returnSettings;

            QueueSettingsChanged(returnSettings, changed);
        }

        // At this point, we've guaranteed that the settings have been generated at least once, so
        // we can proceed with setting ClassRegexHelper#GetTailwindSettings (since that is called only
        // in a synchronous context)
        ClassRegexHelper.GetTailwindSettings ??= GetSettingsAsync;

        return returnSettings;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "Settings consumers can hold locks while requesting settings"
    )]
    private void QueueSettingsChanged(TailwindSettings settings, bool saveChanges = false)
    {
        // Complete settings publication in the background: subscribers can request
        // settings themselves or hold a build/initialization gate in GetSettingsAsync.
        ThreadHelper
            .JoinableTaskFactory.RunAsync(async () =>
            {
                if (!ReferenceEquals(_cachedSettings, settings))
                {
                    return;
                }

                if (saveChanges)
                {
                    await OverrideSettingsAsync(settings);
                }
                else
                {
                    await ApplySettingsAsync(settings);
                }
            })
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/SettingsProvider/SettingsChanged");
    }

    private async Task ApplySettingsAsync(TailwindSettings settings)
    {
        await ProjectConfigurationManager.OnSettingsChangedAsync(settings);
        await NotifySettingsChangedAsync(settings);
    }

    private async Task NotifySettingsChangedAsync(TailwindSettings settings)
    {
        // An older background load must not replace consumers' newer settings.
        if (!ReferenceEquals(_cachedSettings, settings))
        {
            return;
        }

        var listeners = OnSettingsChanged;
        if (listeners is not null)
        {
            await Task.WhenAll(
                listeners
                    .GetInvocationList()
                    .Cast<Func<TailwindSettings, Task>>()
                    .Select(listener => listener(settings))
            );
        }
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
        settings = settings with
        {
            ConfigurationFiles = settings.ConfigurationFiles.RemoveAll(config =>
                string.IsNullOrWhiteSpace(config.Path) || !File.Exists(config.Path)
            ),
            BuildFiles = settings.BuildFiles.RemoveAll(pair =>
                string.IsNullOrWhiteSpace(pair.Input) || !File.Exists(pair.Input)
            ),
        };

        var defaultConfigFile = settings.ConfigurationFiles.FirstOrDefault()?.Path;

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
        var currentSettingsFile = Path.Combine(projectRoot, ExtensionConfigFileName);
        // Intentional: follow a newly selected default config into its project, but
        // preserve an explicit save directory or an unchanged config's current location.
        var relocate =
            preferredSaveDirectory is null
            && desiredProjectRoot != null
            && !string.Equals(
                Path.GetFullPath(Path.Combine(desiredProjectRoot, ExtensionConfigFileName)),
                Path.GetFullPath(currentSettingsFile),
                StringComparison.OrdinalIgnoreCase
            )
            && (
                _cachedSettings is null
                || (defaultConfigFile is not null && defaultConfigFile != oldDefaultConfigFile)
            );
        var saveDirectory = relocate ? desiredProjectRoot! : projectRoot;
        // Resolve relative paths against the final destination, before serializing.
        var projectSettings = TailwindSettingsProjectOnly.FromSettings(settings, saveDirectory);

        if (
            (
                projectSettings.ConfigurationFiles is null
                || projectSettings.ConfigurationFiles.Count == 0
            ) && (projectSettings.BuildFiles is null || projectSettings.BuildFiles.Count == 0)
        )
        {
            // Delete if empty, do not save if it doesn't exist yet

            try
            {
                await _extensionConfigWriteLock.WaitAsync();
                if (File.Exists(currentSettingsFile))
                {
                    try
                    {
                        File.Delete(currentSettingsFile);
                    }
                    catch (Exception ex)
                    {
                        await ex.LogAsync();
                    }
                }
            }
            finally
            {
                _extensionConfigWriteLock.Release();
            }
        }
        else
        {
            try
            {
                await _extensionConfigWriteLock.WaitAsync();
                using (
                    var fs = File.Open(
                        Path.Combine(saveDirectory, ExtensionConfigFileName),
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.ReadWrite
                    )
                )
                {
                    await JsonSerializer.SerializeAsync(
                        fs,
                        projectSettings,
                        options: new() { WriteIndented = true }
                    );
                }

                // Keep the old settings if writing the destination fails.
                if (relocate)
                {
                    try
                    {
                        File.Delete(currentSettingsFile);
                    }
                    catch (Exception ex)
                    {
                        await ex.LogAsync(
                            $"Tailwind CSS: Failed to delete old configuration file at {currentSettingsFile}"
                        );
                    }
                }
            }
            finally
            {
                _extensionConfigWriteLock.Release();
            }
        }

        settings = settings with
        {
            ConfigurationFiles = await ProjectConfigurationManager.ResolveConfigurationFilesAsync(
                settings
            ),
        };
        _cachedSettings = settings;

        await ApplySettingsAsync(settings);
    }

    public void Dispose()
    {
        General.Saved -= GeneralSettingsChanged;
        VS.Events.SolutionEvents.OnAfterOpenFolder -= InvalidateCacheAndSettingsChanged;
        VS.Events.SolutionEvents.OnAfterOpenSolution -= InvalidateCacheAndSettingsChanged;
        VS.Events.DocumentEvents.Saved -= OnFileSaved;
    }

    private async Task<string?> GetDesiredConfigurationDirectoryAsync(string? configPath)
    {
        var projects = (await VS.Solutions.GetAllProjectsAsync()).ToList();

        if (projects.Count == 0 || string.IsNullOrWhiteSpace(configPath))
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
            if (projectRoot is null)
            {
                continue;
            }

            var relativePath = PathHelpers.GetRelativePath(configPath, projectRoot)!;

            if (
                !Path.IsPathRooted(relativePath)
                && !relativePath.StartsWith("..", StringComparison.Ordinal)
            )
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
        // Editor services can request settings before every project has loaded.
        // Return uncached defaults until solution-open initialization can discover the right file.
        if (await VS.Solutions.IsOpeningAsync())
        {
            return null;
        }

        var solution = await VS.Solutions.GetCurrentSolutionAsync();
        var solutionDirectory = Path.GetDirectoryName(solution?.FullPath);
        if (
            solutionDirectory is not null
            && File.Exists(Path.Combine(solutionDirectory, ExtensionConfigFileName))
        )
        {
            return solutionDirectory;
        }

        var projects = (await VS.Solutions.GetAllProjectsAsync(ProjectStateFilter.All))
            .OrderBy(p => p.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (projects.Count == 0)
        {
            var miscProjectPath = await FileFinder.GetCurrentMiscellaneousProjectPathAsync();

            return miscProjectPath;
        }
        else
        {
            var projectDirectories = projects
                .Select(p => Path.GetDirectoryName(p.FullPath))
                .OfType<string>()
                .ToList();
            foreach (var directory in projectDirectories)
            {
                if (File.Exists(Path.Combine(directory, ExtensionConfigFileName)))
                {
                    return directory;
                }
            }

            return projectDirectories.FirstOrDefault(directory =>
                    DefaultConfigurationFileNames.Names.Any(name =>
                        File.Exists(Path.Combine(directory, name))
                    )
                ) ?? projectDirectories.FirstOrDefault();
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
            || settings.ClassCanonicalizationType != origSettings.CanonicalizeClassesType
        )
        {
            var updatedSettings = origSettings with
            {
                EnableTailwindCss = settings.UseTailwindCss,
                DefaultOutputCssName = settings.TailwindOutputFileName,
                OnSaveTriggerFileExtensions = settings
                    .TailwindOnSaveTriggerFileExtensions.Split(';')
                    .ToImmutableList(),
                BuildType = settings.BuildProcessType,
                BuildScript = settings.BuildScript,
                OverrideBuild = settings.OverrideBuild,
                AutomaticallyMinify = settings.AutomaticallyMinify,
                TailwindCliPath = settings.TailwindCliPath,
                SortClassesType = settings.ClassSortType,
                CanonicalizeClassesType = settings.ClassCanonicalizationType,
            };
            _cachedSettings = updatedSettings;
            await ApplySettingsAsync(updatedSettings);
        }
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
        // Package initialization runs on the same open event, before the debounce finishes.
        // It must discover the new solution rather than reload the previous one's settings.
        _cachedSettings = null;
        ThreadHelper
            .JoinableTaskFactory.RunAsync(InvalidateCacheAndSettingsChangedImplAsync)
            .FileAndForget(
                nameof(TailwindCSSIntellisense)
                    + "/SettingsProvider/InvalidateCacheAndSettingsChanged"
            );
    }

    private async Task InvalidateCacheAndSettingsChangedImplAsync()
    {
        _debounceCts.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        try
        {
            await Task.Delay(_invalidationInterval, token);
            await _invocationLock.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            _cachedSettings = null;

            // GetSettingsAsync publishes every newly loaded settings object through
            // the same path, whether discovery was triggered here or by a consumer.
            await GetSettingsAsync();
        }
        finally
        {
            _invocationLock.Release();
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
