using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Node;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions;

/// <summary>
/// Provides project configurations (completion data) for each configuration file.
/// </summary>
[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed partial class ProjectConfigurationManager : IDisposable
{
    [Import]
    internal CompletionConfiguration Configuration { get; set; } = null!;

    [Import]
    internal DirectoryVersionFinder DirectoryVersionFinder { get; set; } = null!;

    [Import]
    internal ProjectConfigurationInitializer ProjectConfigurationInitializer { get; set; } = null!;

    /// <summary>
    /// Completion settings for each project, keyed by configuration file paths.
    /// </summary>
    private readonly Dictionary<string, ProjectCompletionValues> _projectCompletionConfiguration =
        new(StringComparer.OrdinalIgnoreCase);
    private ProjectCompletionValues? _defaultProjectCompletionConfiguration;

    /// <summary>
    /// A cache of file paths to their corresponding project configuration, to speed up lookups for files that don't have an applicable path but are still part of the project.
    /// </summary>
    private readonly Dictionary<
        string,
        ProjectCompletionValues
    > _filePathToProjectConfigurationCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Guards all reads and writes of _projectCompletionConfiguration,
    /// _defaultProjectCompletionConfiguration, and _filePathToProjectConfigurationCache.
    /// SemaphoreSlim(1,1) is used instead of lock because the critical sections contain awaits.
    /// </summary>
    private readonly SemaphoreSlim _configLock = new SemaphoreSlim(1, 1);
    private readonly object _subscriptionLock = new object();
    private readonly SemaphoreSlim _settingsUpdateGate = new(1, 1);
    private long _settingsRevision;
    private long _configurationGeneration;
    private long _nextReloadToken;
    private readonly Dictionary<string, long> _reloadTokens = new(StringComparer.OrdinalIgnoreCase);

    private bool _hasSubscribedToConfigurationChangedEvent;

    private void InitializeEventSubscriptions()
    {
        lock (_subscriptionLock)
        {
            if (!_hasSubscribedToConfigurationChangedEvent)
            {
                Configuration.ConfigurationUpdated += ConfigurationUpdatedAsync;
                _hasSubscribedToConfigurationChangedEvent = true;
            }
        }
    }

    private async Task ConfigurationUpdatedAsync()
    {
        await _configLock.WaitAsync();
        try
        {
            _filePathToProjectConfigurationCache.Clear();
            _configurationGeneration++;
        }
        finally
        {
            _configLock.Release();
        }
    }

    /// <summary>
    /// Returns the ProjectCompletionValues for the given configuration file path.
    /// </summary>
    public async Task<ProjectCompletionValues?> GetCompletionConfigurationByConfigFilePathAsync(
        string configFile
    )
    {
        InitializeEventSubscriptions();
        await ProjectConfigurationInitializer.InitializeAsync();

        await _configLock.WaitAsync();
        try
        {
            return _projectCompletionConfiguration.TryGetValue(configFile, out var value)
                ? value
                : null;
        }
        finally
        {
            _configLock.Release();
        }
    }

    /// <summary>
    /// For IntelliSense; detect which configuration file this file belongs to and return the completion configuration for it. Has built-in caching.
    /// </summary>
    public async Task<ProjectCompletionValues> GetCompletionConfigurationByFilePathAsync(
        string? filePath
    )
    {
        InitializeEventSubscriptions();
        await ProjectConfigurationInitializer.InitializeAsync();
        while (true)
        {
            Dictionary<string, ProjectCompletionValues> snapshot;
            long generation;
            await _configLock.WaitAsync();
            try
            {
                if (
                    string.IsNullOrWhiteSpace(filePath)
                    && _defaultProjectCompletionConfiguration is not null
                )
                    return _defaultProjectCompletionConfiguration;
                if (
                    !string.IsNullOrWhiteSpace(filePath)
                    && _filePathToProjectConfigurationCache.TryGetValue(filePath!, out var cached)
                )
                    return cached;
                snapshot = new Dictionary<string, ProjectCompletionValues>(
                    _projectCompletionConfiguration
                );
                generation = _configurationGeneration;
            }
            finally
            {
                _configLock.Release();
            }

            var config = string.IsNullOrWhiteSpace(filePath)
                ? await ProjectConfigurationInitializer.GetUnsetCompletionConfigurationAsync(
                    TailwindVersion.LATEST
                )
                : await GetCompletionConfigurationByFilePathImplAsync(filePath!, snapshot);
            await _configLock.WaitAsync();
            try
            {
                // A reload may have published while the lookup awaited a fallback.
                // Retry from the new snapshot rather than caching/returning old state.
                if (generation != _configurationGeneration)
                    continue;
                if (!string.IsNullOrWhiteSpace(filePath))
                    _filePathToProjectConfigurationCache[filePath!] = config;
                return config;
            }
            finally
            {
                _configLock.Release();
            }
        }
    }

    private async Task<ProjectCompletionValues> GetCompletionConfigurationByFilePathImplAsync(
        string filePath,
        Dictionary<string, ProjectCompletionValues> projectCompletionConfiguration
    )
    {
        ProjectCompletionValues? closest = null;
        var minDist = int.MaxValue;

        var inputFileDirectories = PathHelpers
            .GetContainingDirectory(filePath)
            .ToLower()
            .Split(Path.DirectorySeparatorChar);

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
                if (k.NotApplicablePaths.Any(p => IsFileInDirectory(filePath, p)))
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
                var applicablePathDirectories = PathHelpers
                    .GetContainingDirectory(path)
                    .ToLower()
                    .Split(Path.DirectorySeparatorChar);

                int inCommon = 0;

                while (
                    inCommon
                        < Math.Min(inputFileDirectories.Length, applicablePathDirectories.Length)
                    && inputFileDirectories[inCommon]
                        .Equals(
                            applicablePathDirectories[inCommon],
                            StringComparison.InvariantCultureIgnoreCase
                        )
                )
                {
                    inCommon++;
                }

                return inputFileDirectories.Length
                    + applicablePathDirectories.Length
                    - 2 * inCommon;
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
                if (k.NotApplicablePaths.Any(p => IsFileInDirectory(filePath, p)))
                {
                    continue;
                }

                if (k.ApplicablePaths.Any(p => IsFileInDirectory(filePath, p)))
                {
                    return k;
                }
            }

            if (k.ApplicablePaths.Any(p => PathHelpers.PathMatchesGlob(filePath, p)))
            {
                return k;
            }
        }

        return await ProjectConfigurationInitializer.GetUnsetCompletionConfigurationAsync(
            TailwindVersion.LATEST
        );
    }

    private static bool IsFileInDirectory(string filePath, string directory)
    {
        var normalizedFile = filePath.Replace('\\', '/');
        var normalizedDirectory = directory.Replace('\\', '/').TrimEnd('/');
        return normalizedFile.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase)
            || normalizedFile.StartsWith(
                normalizedDirectory + "/",
                StringComparison.OrdinalIgnoreCase
            );
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
        var revision = Interlocked.Increment(ref _settingsRevision);
        var files = settings
            .ConfigurationFiles.Select(f => new ConfigurationFile { Path = f.Path })
            .ToList();
        var builds = settings.BuildFiles.ToArray();
        await TaskScheduler.Default;
        await _settingsUpdateGate.WaitAsync();
        try
        {
            if (revision != Interlocked.Read(ref _settingsRevision))
                return;
            foreach (var buildFile in builds)
            {
                if (
                    files.All(cf =>
                        !cf.Path.Equals(buildFile.Input, StringComparison.OrdinalIgnoreCase)
                    )
                    && await DirectoryVersionFinder.GetTailwindVersionAsync(
                        buildFile.Input,
                        settings
                    ) >= TailwindVersion.V4
                )
                    files.Add(new() { Path = buildFile.Input });
            }

            var current = (await GetAllProjectCompletionValuesAsync()).ToDictionary(
                p => p.FilePath,
                StringComparer.OrdinalIgnoreCase
            );
            var staged = new Dictionary<string, ProjectCompletionValues>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var file in files)
            {
                if (revision != Interlocked.Read(ref _settingsRevision))
                    return;
                var version = await DirectoryVersionFinder.GetTailwindVersionAsync(
                    file.Path,
                    settings
                );
                if (
                    (!current.TryGetValue(file.Path, out var old) || old.Version != version)
                    && (!settings.UseCli || string.IsNullOrWhiteSpace(settings.TailwindCliPath))
                )
                {
                    await CheckForUpdates.UpdateConfigFileFolderAsync(file.Path);
                    DirectoryVersionFinder.ClearCacheForDirectory(
                        PathHelpers.GetContainingDirectory(file.Path)
                    );
                }
                var loaded = await Configuration.CreateCompletionSnapshotAsync(file, version);
                // A failed reload leaves the last complete snapshot available.
                staged[file.Path] =
                    loaded
                    ?? old
                    ?? CreateDefaultSnapshot(
                        (
                            await ProjectConfigurationInitializer.GetUnsetCompletionConfigurationAsync(
                                version
                            )
                        ).Copy(),
                        file.Path
                    );
            }

            await _configLock.WaitAsync();
            try
            {
                if (revision != Interlocked.Read(ref _settingsRevision))
                    return;
                _projectCompletionConfiguration.Clear();
                foreach (var pair in staged)
                    _projectCompletionConfiguration[pair.Key] = pair.Value;
                _defaultProjectCompletionConfiguration =
                    files
                        .Select(f => staged[f.Path])
                        .FirstOrDefault(p => p.ApplicablePaths.Count > 0)
                    ?? staged.Values.FirstOrDefault();
                _reloadTokens.Clear();
                _filePathToProjectConfigurationCache.Clear();
                _configurationGeneration++;
                settings.ConfigurationFiles = files;
                // The import map is updated under the same publication gate.
                // Reloader releases its own gate before it requests a reload.
                await Configuration.Reloader.ReplaceAllImportsAsync(staged.Values);
            }
            finally
            {
                _configLock.Release();
            }
            await Configuration.NotifyConfigurationUpdatedAsync();
        }
        finally
        {
            _settingsUpdateGate.Release();
        }
    }

    private static ProjectCompletionValues CreateDefaultSnapshot(
        ProjectCompletionValues values,
        string path
    )
    {
        values.FilePath = path;
        return values;
    }

    internal sealed class ReloadRequest(
        ProjectCompletionValues original,
        long settingsRevision,
        long token
    )
    {
        internal ProjectCompletionValues Original { get; } = original;
        internal long SettingsRevision { get; } = settingsRevision;
        internal long Token { get; } = token;
    }

    internal async Task<ReloadRequest?> BeginReloadAsync(string path)
    {
        await _configLock.WaitAsync();
        try
        {
            if (!_projectCompletionConfiguration.TryGetValue(path, out var project))
                return null;
            var token = ++_nextReloadToken;
            _reloadTokens[path] = token;
            return new(project, Interlocked.Read(ref _settingsRevision), token);
        }
        finally
        {
            _configLock.Release();
        }
    }

    internal async Task<bool> TryPublishReloadAsync(
        ReloadRequest request,
        ProjectCompletionValues replacement
    )
    {
        await _configLock.WaitAsync();
        try
        {
            var path = request.Original.FilePath;
            if (
                request.SettingsRevision != Interlocked.Read(ref _settingsRevision)
                || !_reloadTokens.TryGetValue(path, out var token)
                || token != request.Token
                || !_projectCompletionConfiguration.TryGetValue(path, out var current)
                || !ReferenceEquals(current, request.Original)
            )
                return false;
            _projectCompletionConfiguration[path] = replacement;
            if (ReferenceEquals(_defaultProjectCompletionConfiguration, current))
                _defaultProjectCompletionConfiguration = replacement;
            _filePathToProjectConfigurationCache.Clear();
            _configurationGeneration++;
            await Configuration.Reloader.ReplaceImportsAsync(
                new() { Path = path },
                replacement.LoadedConfiguration?.Imports ?? []
            );
            return true;
        }
        finally
        {
            _configLock.Release();
        }
    }

    public void Dispose()
    {
        lock (_subscriptionLock)
        {
            if (_hasSubscribedToConfigurationChangedEvent)
            {
                Configuration.ConfigurationUpdated -= ConfigurationUpdatedAsync;
            }
        }
    }
}
