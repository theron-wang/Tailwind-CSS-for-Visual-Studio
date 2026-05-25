using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// Returns the ProjectCompletionValues for the given configuration file path.
    /// </summary>
    public async Task<ProjectCompletionValues?> GetCompletionConfigurationByConfigFilePathAsync(string configFile)
    {
        await ProjectConfigurationInitializer.InitializeAsync();
        return _projectCompletionConfiguration.TryGetValue(configFile.ToLower(), out var value) ? value : null;
    }

    /// <summary>
    /// For IntelliSense; detect which configuration file this file belongs to and return the completion configuration for it. Has built-in caching.
    /// </summary>
    public async Task<ProjectCompletionValues> GetCompletionConfigurationByFilePathAsync(string? filePath)
    {
        await ProjectConfigurationInitializer.InitializeAsync();

        if (filePath is null)
        {
            if (_defaultProjectCompletionConfiguration is not null)
            {
                return _defaultProjectCompletionConfiguration;
            }
            else
            {
                // Default to v4
                return await ProjectConfigurationInitializer.GetUnsetCompletionConfigurationAsync(TailwindVersion.LATEST);
            }
        }

        if (_filePathToProjectConfigurationCache.TryGetValue(filePath.ToLower(), out var cached))
        {
            return cached;
        }

        ProjectCompletionValues config = await GetCompletionConfigurationByFilePathImplAsync(filePath);
        _filePathToProjectConfigurationCache[filePath.ToLower()] = config;
        return config;
    }

    private async Task<ProjectCompletionValues> GetCompletionConfigurationByFilePathImplAsync(string filePath)
    {
        ProjectCompletionValues? closest = null;
        var minDist = int.MaxValue;

        var inputFileDirectories = Path.GetDirectoryName(filePath).ToLower().Split(Path.DirectorySeparatorChar);

        foreach (var k in _projectCompletionConfiguration.Values)
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

        foreach (var k in _projectCompletionConfiguration.Values)
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
        return _projectCompletionConfiguration.Values;
    }

    public async Task OnSettingsChangedAsync(TailwindSettings settings)
    {
        _defaultProjectCompletionConfiguration = null;
        _filePathToProjectConfigurationCache.Clear();

        // V4 uses BuildFiles as ConfigurationFiles. Since existing code already uses ConfigurationFiles, it's easier to just
        // populate it here rather than rewrite everything to account for BuildFiles.
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

        foreach (var file in settings.ConfigurationFiles)
        {
            if (!_projectCompletionConfiguration.TryGetValue(file.Path.ToLower(), out var projectConfig))
            {
                if (!settings.UseCli || string.IsNullOrWhiteSpace(settings.TailwindCliPath))
                {
                    await CheckForUpdates.UpdateConfigFileFolderAsync(file.Path);
                    DirectoryVersionFinder.ClearCacheForDirectory(Path.GetDirectoryName(file.Path));
                }

                var version = await DirectoryVersionFinder.GetTailwindVersionAsync(file.Path, settings);

                projectConfig = (await ProjectConfigurationInitializer.GetUnsetCompletionConfigurationAsync(version)).Copy();
                _projectCompletionConfiguration[file.Path.ToLower()] = projectConfig;
            }

            projectConfig.FilePath = file.Path.ToLower();
        }

        var toRemove = _projectCompletionConfiguration.Keys.Except(settings.ConfigurationFiles.Select(f => f.Path.ToLower())).ToList();

        foreach (var file in toRemove)
        {
            _projectCompletionConfiguration.Remove(file);
        }

        if (settings.ConfigurationFiles.Count > 0)
        {
            foreach (var config in settings.ConfigurationFiles)
            {
                if (_projectCompletionConfiguration[config.Path.ToLower()].ApplicablePaths.Count > 0)
                {
                    _defaultProjectCompletionConfiguration = _projectCompletionConfiguration[settings.ConfigurationFiles.First().Path.ToLower()];
                    break;
                }
            }

            _defaultProjectCompletionConfiguration ??= _projectCompletionConfiguration[settings.ConfigurationFiles.First().Path.ToLower()];
        }

        await Configuration.ReloadCustomAttributesAsync(settings);
    }
}
