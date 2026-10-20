using System;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Helpers;

/// <summary>
/// It is unsafe to import SettingsProvider in this class. First, importing it here will cause a circular dependency;
/// second, it will cause infinite recursion if you try to get settings in any of these methods.
/// </summary>
[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
internal class DirectoryVersionFinder : IDisposable
{
    public DirectoryVersionFinder()
    {
        VS.Events.SolutionEvents.OnAfterOpenFolder += InvalidateCache;
        VS.Events.SolutionEvents.OnAfterOpenProject += InvalidateCache;
    }

    private readonly ConcurrentDictionary<string, TailwindVersion> _cache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<string, bool> _installedCache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<string, long> _pendingInstallChecks = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly object _cacheGate = new();
    private long _cacheGeneration;

    /// <summary>
    /// Gets whether Tailwind is installed in the given directory, if available. If not available, returns null and populates the cache in the background.
    /// </summary>
    /// <param name="directory">The directory to check for Tailwind installation.</param>
    /// <param name="settings">The settings to use for the check.</param>
    /// <returns>Whether Tailwind is installed, or null if not available.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget is ok"
    )]
    public bool? GetCachedTailwindInstallationStatusOrPopulateInBackground(
        string? directory,
        TailwindSettings settings
    )
    {
        if (settings.UseCli && !string.IsNullOrWhiteSpace(settings.TailwindCliPath))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        directory = directory!.ToLower();

        long generation;
        lock (_cacheGate)
        {
            if (_installedCache.TryGetValue(directory, out var value))
            {
                return value;
            }
            generation = _cacheGeneration;
            if (!_pendingInstallChecks.TryAdd(directory, generation))
            {
                return null;
            }
        }

        ThreadHelper
            .JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var installed = await IsTailwindInstalledAsync(directory!)
                        .ConfigureAwait(false);
                    lock (_cacheGate)
                    {
                        if (generation == _cacheGeneration)
                        {
                            _installedCache[directory!] = installed;
                        }
                    }
                }
                finally
                {
                    lock (_cacheGate)
                    {
                        if (
                            _pendingInstallChecks.TryGetValue(directory!, out var pendingGeneration)
                            && generation == pendingGeneration
                        )
                        {
                            _pendingInstallChecks.TryRemove(directory!, out _);
                        }
                    }
                }
            })
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/PopulateVersionCache");

        return null;
    }

    /// <summary>
    /// Checks whether Tailwind is installed without publishing mutable cache state.
    /// </summary>
    protected virtual async Task<bool> IsTailwindInstalledAsync(string directory)
    {
        var processInfo = new ProcessStartInfo()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            FileName = "cmd",
            Arguments = "/C npm list tailwindcss @tailwindcss/cli --depth=0",
            WorkingDirectory = directory,
            StandardOutputEncoding = Encoding.UTF8,
        };

        string output;

        using (
            var process = (
                Process.Start(processInfo)
                ?? throw new InvalidOperationException(
                    "Unable to start the Tailwind command process."
                )
            )
        )
        {
            output = await process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();
        }

        // If not found locally, default to global
        if (string.IsNullOrWhiteSpace(output) || !output.Contains("tailwindcss"))
        {
            processInfo = new ProcessStartInfo()
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                FileName = "cmd",
                Arguments = "/C npm list tailwindcss @tailwindcss/cli --depth=0 -g",
                WorkingDirectory = directory,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var process = (
                Process.Start(processInfo)
                ?? throw new InvalidOperationException(
                    "Unable to start the Tailwind command process."
                )
            );

            output = await process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();
        }

        // Sample output: `-- tailwindcss@4.0.0
        if (!string.IsNullOrWhiteSpace(output))
        {
            if (output.Contains("@tailwindcss/cli"))
            {
                return true;
            }

            if (output.Contains("tailwindcss") && !output.Contains("tailwindcss@4"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the cached tailwind version for the directory of the given file, if available. If not available, returns null and populates the cache in the background.
    /// </summary>
    /// <param name="file">The file for which to get the tailwind version.</param>
    /// <param name="settings">The Tailwind settings.</param>
    /// <returns>The cached tailwind version, or null if not available.</returns>
    ///
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget is ok"
    )]
    public TailwindVersion? GetCachedTailwindVersionAndPopulateInBackground(
        string file,
        TailwindSettings settings
    )
    {
        var directory = PathHelpers.GetContainingDirectory(file).ToLower();

        if (_cache.TryGetValue(directory, out var version))
        {
            return version;
        }

        ThreadHelper
            .JoinableTaskFactory.RunAsync(() => PopulateVersionCacheAsync(directory, settings))
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/PopulateVersionCache");

        return null;
    }

    /// <summary>
    /// Gets the tailwind version for the directory of the given file. Caches the result, until a new project is opened.
    /// </summary>
    /// <param name="file">The file for which to get the tailwind version.</param>
    /// <param name="settings">The Tailwind settings (see summary for this class for justification)</param>
    public async Task<TailwindVersion> GetTailwindVersionAsync(
        string file,
        TailwindSettings settings
    )
    {
        var directory = PathHelpers.GetContainingDirectory(file).ToLower();

        if (_cache.TryGetValue(directory, out var version))
        {
            return version;
        }

        return await PopulateVersionCacheAsync(directory, settings);
    }

    private async Task<TailwindVersion> PopulateVersionCacheAsync(
        string directory,
        TailwindSettings settings
    )
    {
        long generation;
        lock (_cacheGate)
        {
            generation = _cacheGeneration;
        }
        // There's a weird bug (cause unknown) where we end up adding null directories to the cache, and we don't want that
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return TailwindVersion.LATEST;
        }

        var isCli = settings.UseCli && !string.IsNullOrWhiteSpace(settings.TailwindCliPath);

        var processInfo = new ProcessStartInfo()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            FileName = "cmd",
            Arguments = isCli
                ? $"/C \"{settings.TailwindCliPath}\" --help"
                : "/C npm list tailwindcss --depth=0",
            WorkingDirectory = directory,
            StandardOutputEncoding = Encoding.UTF8,
        };

        string output;

        using (
            var process = (
                Process.Start(processInfo)
                ?? throw new InvalidOperationException(
                    "Unable to start the Tailwind command process."
                )
            )
        )
        {
            output = await process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();
        }

        // If not found locally, default to global
        if (!isCli && (string.IsNullOrWhiteSpace(output) || !output.Contains("tailwindcss")))
        {
            processInfo = new ProcessStartInfo()
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                FileName = "cmd",
                Arguments = "/C npm list tailwindcss --depth=0 -g",
                WorkingDirectory = directory,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var process = (
                Process.Start(processInfo)
                ?? throw new InvalidOperationException(
                    "Unable to start the Tailwind command process."
                )
            );

            output = await process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();
        }

        // Sample output: `-- tailwindcss@4.0.0
        if (!string.IsNullOrWhiteSpace(output))
        {
            if (output.Contains("@3") || output.Contains("v3"))
            {
                return CacheVersion(directory, TailwindVersion.V3, generation);
            }
            else if (output.Contains("@4.0") || output.Contains("v4.0"))
            {
                return CacheVersion(directory, TailwindVersion.V4, generation);
            }
            else if (output.Contains("@4.1") || output.Contains("v4.1"))
            {
                return CacheVersion(directory, TailwindVersion.V4_1, generation);
            }
            else if (output.Contains("@4.2") || output.Contains("v4.2"))
            {
                return CacheVersion(directory, TailwindVersion.V4_2, generation);
            }
        }

        return CacheVersion(directory, TailwindVersion.LATEST, generation);
    }

    private TailwindVersion CacheVersion(string directory, TailwindVersion version, long generation)
    {
        lock (_cacheGate)
        {
            if (generation == _cacheGeneration)
            {
                _cache[directory] = version;
            }
        }
        return version;
    }

    public void ClearCacheForDirectory(string? directory, bool recursive = true)
    {
        if (directory is null)
        {
            return;
        }

        directory = directory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLower();

        lock (_cacheGate)
        {
            // Invalidate in-flight checks too. An older completion must neither
            // republish a stale result nor remove a new generation's pending check.
            _cacheGeneration++;
            _pendingInstallChecks.Clear();
            if (recursive)
            {
                foreach (var key in _cache.Keys.Concat(_installedCache.Keys).Distinct().ToList())
                {
                    if (
                        key.Equals(directory, StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith(
                            directory + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase
                        )
                        || key.StartsWith(
                            directory + Path.AltDirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        _cache.TryRemove(key, out _);
                        _installedCache.TryRemove(key, out _);
                    }
                }
            }
            else
            {
                _cache.TryRemove(directory, out _);
                _installedCache.TryRemove(directory, out _);
            }
        }
    }

    private void InvalidateCache(string? _)
    {
        lock (_cacheGate)
        {
            _cacheGeneration++;
            _pendingInstallChecks.Clear();
            _cache.Clear();
            _installedCache.Clear();
        }
    }

    private void InvalidateCache(Project? _)
    {
        InvalidateCache((string?)null);
    }

    public void Dispose()
    {
        VS.Events.SolutionEvents.OnAfterOpenFolder -= InvalidateCache;
        VS.Events.SolutionEvents.OnAfterOpenProject -= InvalidateCache;
    }
}
