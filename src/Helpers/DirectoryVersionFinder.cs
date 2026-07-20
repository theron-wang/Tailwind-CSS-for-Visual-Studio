using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
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

    private readonly Dictionary<string, TailwindVersion> _cache = [];
    private readonly Dictionary<string, bool> _installedCache = [];

    /// <summary>
    /// Gets the tailwind version for the directory of the given file. Caches the result, until a new project is opened.
    /// </summary>
    public async Task<bool> IsTailwindInstalledAsync(string? directory, TailwindSettings settings)
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

        if (_installedCache.TryGetValue(directory, out var value))
        {
            return value;
        }

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

        using (var process = Process.Start(processInfo))
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

            using var process = Process.Start(processInfo);

            output = await process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();
        }

        // Sample output: `-- tailwindcss@4.0.0
        if (!string.IsNullOrWhiteSpace(output))
        {
            if (output.Contains("@tailwindcss/cli"))
            {
                _installedCache[directory] = true;
                return true;
            }

            if (output.Contains("tailwindcss@4") && output.Contains("@tailwindcss/cli"))
            {
                _installedCache[directory] = true;
                return true;
            }

            if (output.Contains("tailwindcss") && !output.Contains("tailwindcss@4"))
            {
                _installedCache[directory] = true;
                return true;
            }
        }

        _installedCache[directory] = false;
        return false;
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
        var directory = Path.GetDirectoryName(file).ToLower();

        if (_cache.TryGetValue(directory, out var version))
        {
            return version;
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

        using (var process = Process.Start(processInfo))
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

            using var process = Process.Start(processInfo);

            output = await process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();
        }

        // Sample output: `-- tailwindcss@4.0.0
        if (!string.IsNullOrWhiteSpace(output))
        {
            if (output.Contains("@3") || output.Contains("v3"))
            {
                _cache[directory] = TailwindVersion.V3;
                return TailwindVersion.V3;
            }
            else if (output.Contains("@4.0") || output.Contains("v4.0"))
            {
                _cache[directory] = TailwindVersion.V4;
                return TailwindVersion.V4;
            }
            else if (output.Contains("@4.1") || output.Contains("v4.1"))
            {
                _cache[directory] = TailwindVersion.V4_1;
                return TailwindVersion.V4_1;
            }
            else if (output.Contains("@4.2") || output.Contains("v4.2"))
            {
                _cache[directory] = TailwindVersion.V4_2;
                return TailwindVersion.V4_2;
            }
        }

        _cache[directory] = TailwindVersion.LATEST;
        return TailwindVersion.LATEST;
    }

    public void ClearCacheForDirectory(string directory, bool recursive = true)
    {
        if (directory is null)
        {
            return;
        }

        directory = directory.ToLower();

        if (recursive)
        {
            foreach (var key in _cache.Keys.ToList())
            {
                if (key.StartsWith(directory))
                {
                    _cache.Remove(key);
                    _installedCache.Remove(key);
                }
            }
        }
        else
        {
            _cache.Remove(directory);
            _installedCache.Remove(directory);
        }
    }

    private void InvalidateCache(string? _)
    {
        _cache.Clear();
    }

    private void InvalidateCache(Project? _)
    {
        _cache.Clear();
    }

    public void Dispose()
    {
        VS.Events.SolutionEvents.OnAfterOpenFolder -= InvalidateCache;
        VS.Events.SolutionEvents.OnAfterOpenProject -= InvalidateCache;
    }
}
