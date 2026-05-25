using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Node;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions;

/// <summary>
/// Provides project configurations (completion data) for each configuration file.
/// </summary>
[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ProjectConfigurationManager
{
    [Import]
    internal CompletionConfiguration Configuration { get; set; } = null!;
    [Import]
    internal DirectoryVersionFinder DirectoryVersionFinder { get; set; } = null!;
    [Import]
    internal ProjectConfigurationInitializer ProjectConfigurationInitializer { get; set; } = null!;
    [Import]
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    internal static ImageSource TailwindLogo { get; private set; } = new BitmapImage(new Uri(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources", "tailwindlogo.png"), UriKind.Relative));

    /// <summary>
    /// Completion settings for each project, keyed by configuration file paths.
    /// </summary>
    private readonly Dictionary<string, ProjectCompletionValues> _projectCompletionConfiguration = [];
    private ProjectCompletionValues? _defaultProjectCompletionConfiguration;

    /// <summary>
    /// A cache of file paths to their corresponding project configuration, to speed up lookups for files that don't have an applicable path but are still part of the project.
    /// </summary>
    private readonly Dictionary<string, ProjectCompletionValues> _filePathToProjectConfigurationCache = [];

    /// <summary>
    /// Guards all reads and writes of _projectCompletionConfiguration,
    /// _defaultProjectCompletionConfiguration, and _filePathToProjectConfigurationCache.
    /// SemaphoreSlim(1,1) is used instead of lock because the critical sections contain awaits.
    /// </summary>
    private readonly SemaphoreSlim _configLock = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Returns the ProjectCompletionValues for the given configuration file path.
    /// </summary>
    public async Task<ProjectCompletionValues?> GetCompletionConfigurationByConfigFilePathAsync(string configFile)
    {
        await ProjectConfigurationInitializer.InitializeAsync();

        await _configLock.WaitAsync();
        try
        {
            return _projectCompletionConfiguration.TryGetValue(configFile.ToLower(), out var value) ? value : null;
        }
        finally
        {
            _configLock.Release();
        }
    }

    /// <summary>
    /// For IntelliSense; detect which configuration file this file belongs to and return the completion configuration for it. Has built-in caching.
    /// </summary>
    public async Task<ProjectCompletionValues> GetCompletionConfigurationByFilePathAsync(string? filePath)
    {
        await ProjectConfigurationInitializer.InitializeAsync();

        await _configLock.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                if (_defaultProjectCompletionConfiguration is not null)
                {
                    return _defaultProjectCompletionConfiguration;
                }
            }
            else if (_filePathToProjectConfigurationCache.TryGetValue(filePath!.ToLower(), out var cached))
            {
                return cached;
            }
        }
        finally
        {
            _configLock.Release();
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            // Default to v4 — no state access needed here
            return await ProjectConfigurationInitializer.GetUnsetCompletionConfigurationAsync(TailwindVersion.LATEST);
        }

        // Compute the result outside the lock (may await); snapshot the dictionary first.
        Dictionary<string, ProjectCompletionValues> snapshot;
        await _configLock.WaitAsync();
        try
        {
            snapshot = new Dictionary<string, ProjectCompletionValues>(_projectCompletionConfiguration);
        }
        finally
        {
            _configLock.Release();
        }

        ProjectCompletionValues config = await GetCompletionConfigurationByFilePathImplAsync(filePath!, snapshot);

        await _configLock.WaitAsync();
        try
        {
            // Re-check cache in case a concurrent call already populated it.
            if (!_filePathToProjectConfigurationCache.TryGetValue(filePath!.ToLower(), out var existing))
            {
                _filePathToProjectConfigurationCache[filePath.ToLower()] = config;
            }
            else
            {
                config = existing;
            }
        }
        finally
        {
            _configLock.Release();
        }

        return config;
    }

    private async Task<ProjectCompletionValues> GetCompletionConfigurationByFilePathImplAsync(
        string filePath,
        Dictionary<string, ProjectCompletionValues> projectCompletionConfiguration)
    {
        ProjectCompletionValues? closest = null;
        var minDist = int.MaxValue;

        var inputFileDirectories = Path.GetDirectoryName(filePath).ToLower().Split(Path.DirectorySeparatorChar);

        foreach (var k in projectCompletionConfiguration.Values)
        {
            if (!k.ApplicablePaths.Any())
            {
                continue;
            }

            if (k.FilePath.Equals(filePath, StringComparison.InvariantCultureIgnoreCase))
            {
                return k;
            }

            if (k.Version >= TailwindVersion.V4)
            {
                if (k.NotApplicablePaths.Any(p => filePath.StartsWith(p, StringComparison.InvariantCultureIgnoreCase)))
                {
                    continue;
                }
            }
            else if (k.ApplicablePaths.Any(p => PathHelpers.PathMatchesGlob(filePath, p)))
            {
                return k;
            }

            var distance = k.ApplicablePaths.Min(path =>
            {
                // Find the distance from this configuration file to filePath.
                // i.e., find the number of subdirectories that differ between the two paths
                var applicablePathDirectories = Path.GetDirectoryName(path).ToLower().Split(Path.DirectorySeparatorChar);

                int inCommon = 0;

                while (inCommon < Math.Min(inputFileDirectories.Length, applicablePathDirectories.Length) && inputFileDirectories[inCommon].Equals(applicablePathDirectories[inCommon], StringComparison.InvariantCultureIgnoreCase))
                {
                    inCommon++;
                }

                return inputFileDirectories.Length + applicablePathDirectories.Length - 2 * inCommon;
            });

            if (distance < minDist)
            {
                minDist = distance;
                closest = k;
            }
        }

        // Find the closest one, if possible
        if (closest is not null)
        {
            return closest;
        }

        foreach (var k in projectCompletionConfiguration.Values)
        {
            if (k.FilePath.Equals(filePath, StringComparison.InvariantCultureIgnoreCase))
            {
                return k;
            }

            if (k.Version >= TailwindVersion.V4)
            {
                if (k.NotApplicablePaths.Any(p => filePath.StartsWith(p, StringComparison.InvariantCultureIgnoreCase)))
                {
                    continue;
                }

                if (k.ApplicablePaths.Any(p => filePath.StartsWith(p, StringComparison.InvariantCultureIgnoreCase)))
                {
                    return k;
                }
            }

            if (k.ApplicablePaths.Any(p => PathHelpers.PathMatchesGlob(filePath, p)))
            {
                return k;
            }
        }

        return await ProjectConfigurationInitializer.GetUnsetCompletionConfigurationAsync(TailwindVersion.LATEST);
    }

    public async Task<IEnumerable<ProjectCompletionValues>> GetAllProjectCompletionValuesAsync()
    {
        await _configLock.WaitAsync();
        try
        {
            // Return a snapshot so callers enumerate a stable copy.
            return _projectCompletionConfiguration.Values.ToList();
        }
        finally
        {
            _configLock.Release();
        }
    }

    public async Task OnSettingsChangedAsync(TailwindSettings settings)
    {
        // Perform all async I/O (version detection, config file updates) before
        // acquiring the lock so we hold it only while mutating shared state.
        foreach (var buildFile in settings.BuildFiles)
        {
            if (settings.ConfigurationFiles.All(cf => !cf.Path.Equals(buildFile.Input, StringComparison.InvariantCultureIgnoreCase)))
            {
                var version = await DirectoryVersionFinder.GetTailwindVersionAsync(buildFile.Input, settings);

                if (version >= TailwindVersion.V4)
                {
                    settings.ConfigurationFiles.Add(new() { Path = buildFile.Input.ToLower() });
                }
            }
        }

        // Pre-compute new configs outside the lock (these involve awaits).
        var newConfigs = new Dictionary<string, ProjectCompletionValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in settings.ConfigurationFiles)
        {
            var key = file.Path.ToLower();

            // Check existing config under the lock briefly.
            ProjectCompletionValues? existingConfig;
            await _configLock.WaitAsync();
            try
            {
                _projectCompletionConfiguration.TryGetValue(key, out existingConfig);
            }
            finally
            {
                _configLock.Release();
            }

            ProjectCompletionValues projectConfig;
            if (existingConfig is null)
            {
                if (!settings.UseCli || string.IsNullOrWhiteSpace(settings.TailwindCliPath))
                {
                    await CheckForUpdates.UpdateConfigFileFolderAsync(file.Path);
                    DirectoryVersionFinder.ClearCacheForDirectory(Path.GetDirectoryName(file.Path));
                }

                var version = await DirectoryVersionFinder.GetTailwindVersionAsync(file.Path, settings);
                projectConfig = (await ProjectConfigurationInitializer.GetUnsetCompletionConfigurationAsync(version)).Copy();
            }
            else
            {
                projectConfig = existingConfig;
            }

            projectConfig.FilePath = key;
            newConfigs[key] = projectConfig;
        }

        // Now atomically swap in all new state under the lock.
        await _configLock.WaitAsync();
        try
        {
            _defaultProjectCompletionConfiguration = null;
            _filePathToProjectConfigurationCache.Clear();

            var toRemove = _projectCompletionConfiguration.Keys
                .Except(newConfigs.Keys, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var key in toRemove)
            {
                _projectCompletionConfiguration.Remove(key);
            }

            foreach (var kvp in newConfigs)
            {
                _projectCompletionConfiguration[kvp.Key] = kvp.Value;
            }

            if (settings.ConfigurationFiles.Count > 0)
            {
                foreach (var config in settings.ConfigurationFiles)
                {
                    var key = config.Path.ToLower();
                    if (_projectCompletionConfiguration.TryGetValue(key, out var pcv) && pcv.ApplicablePaths.Count > 0)
                    {
                        _defaultProjectCompletionConfiguration = _projectCompletionConfiguration[settings.ConfigurationFiles.First().Path.ToLower()];
                        break;
                    }
                }

                _defaultProjectCompletionConfiguration ??= _projectCompletionConfiguration[settings.ConfigurationFiles.First().Path.ToLower()];
            }
        }
        finally
        {
            _configLock.Release();
        }

        await Configuration.ReloadCustomAttributesAsync(settings);
    }
}