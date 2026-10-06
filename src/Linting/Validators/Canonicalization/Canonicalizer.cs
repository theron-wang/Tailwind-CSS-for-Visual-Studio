using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Build;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Canonicalization;

/// <summary>
/// A class that handles canonicalization of Tailwind CSS class names.
/// </summary>
[Export(typeof(Canonicalizer))]
[PartCreationPolicy(CreationPolicy.Shared)]
internal class Canonicalizer : TailwindCliInterop
{
    [Import]
    private ProjectConfigurationManager _projectConfigurationManager = null!;

    /// <summary>
    /// A cache for storing previously canonicalized class names.
    /// </summary>
    /// <remarks>
    /// The inner dictionary maps a class name to potential canonicalizations.
    /// </remarks>
    private readonly ConcurrentDictionary<
        ProjectCompletionValues,
        ConcurrentDictionary<string, List<Canonicalization>>
    > _canonicalizationCache = [];

    private ConcurrentDictionary<ProjectCompletionValues, Process> _processes = [];
    private readonly ConcurrentDictionary<ProjectCompletionValues, SemaphoreSlim> _processGates =
    [];
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly object _processLifecycleLock = new();
    private bool _disposed;

    /// <summary>
    /// Given a list of class names, this method returns their canonicalized versions.
    /// </summary>
    /// <param name="classNames">The list of class names to canonicalize.</param>
    /// <param name="filePath">The path to the file containing the class names.</param>
    /// <returns>A list of canonicalized class names.</returns>
    public async Task<List<Canonicalization>> CanonicalizeAsync(
        List<string> classNames,
        string filePath
    )
    {
        var project = await _projectConfigurationManager.GetCompletionConfigurationByFilePathAsync(
            filePath
        );

        // Canonicalization via the CLI was only added in Tailwind v4.2, so we can't do anything earlier
        if (project.Version < TailwindVersion.V4_2)
        {
            return [];
        }

        var cache = _canonicalizationCache.GetOrAdd(
            project,
            _ => new ConcurrentDictionary<string, List<Canonicalization>>()
        );

        // Any class with a space is a razor class, and we get rid of it so we don't mess up the canonicalizer.
        // This is a heuristic; there are razor classes without spaces, but those can just be canonicalized normally,
        // since it won't mess up the canonicalizer.
        var classNamesSet = classNames.Where(c => !c.Contains(' ')).ToFrozenSet();

        // Alias to make it more clear
        var worklist = classNames;

        var result = new List<Canonicalization>();
        IEnumerable<Canonicalization>? processCanonicalizations = null;

        // MUST REMOVE
        cache.Clear();

        while (worklist.Count > 0)
        {
            var className = worklist[0];
            worklist.RemoveAt(0);

            // A razor class cannot be canonicalized.
            if (className.Contains(' '))
            {
                continue;
            }

            if (!cache.TryGetValue(className, out var cacheEntry))
            {
                processCanonicalizations ??= await FetchCanonicalizationsFromProcessAsync(
                    classNamesSet,
                    filePath
                );

                cacheEntry = [.. processCanonicalizations.Where(c => c.ClassNames[0] == className)];
            }

            // Find the best match; i.e., the canonicalization with the biggest number of class names but which is also a subset.
            // But also, do not include canonicalizations that are just the class name itself, because that would be a no-op.
            var bestMatch = cacheEntry
                .Where(c =>
                    classNamesSet.IsSupersetOf(c.ClassNames) && c.ClassNames.Length > 1
                    || c.CanonicalizedClassName != c.ClassNames[0]
                )
                .OrderByDescending(c => c.ClassNames.Length)
                .FirstOrDefault();

            // If null, then that means we don't have a canonicalization for this class name, so we just return the class name itself.
            // For example, if the class name is "px-2" and we have a canonicalization for "px-2 py-2" but not for "px-2" itself, then we just return "px-2".
            if (bestMatch != null)
            {
                result.Add(bestMatch);

                // Skip 1 because the first one is always the current className, which we already removed from the worklist.
                foreach (var classNameToAdd in bestMatch.ClassNames.Skip(1))
                {
                    worklist.Remove(classNameToAdd);
                }
            }
        }

        if (processCanonicalizations is not null)
        {
            foreach (var canonicalization in processCanonicalizations)
            {
                cache.AddOrUpdate(
                    canonicalization.ClassNames[0],
                    _ => [canonicalization],
                    (_, existing) =>
                    {
                        existing.Add(canonicalization);
                        return existing;
                    }
                );
            }
        }

        return result;
    }

    /// <summary>
    /// Fetches canonicalizations from the Tailwind CLI process for a given set of class names and a file path.
    /// </summary>
    /// <param name="classNames">The set of class names to fetch canonicalizations for.</param>
    /// <param name="filePath">The path to the file containing the class names.</param>
    /// <returns>A list of canonicalizations.</returns>
    private async Task<IEnumerable<Canonicalization>> FetchCanonicalizationsFromProcessAsync(
        IEnumerable<string> classNames,
        string filePath
    )
    {
        // Algorithm overview:
        // 1. Given the set of class names (C), run the canonicalization across the whole list
        // 2. For each class (ci) in C, run the canonicalization across the list C - {ci}. If the canonicalization is different,
        //    then we'll keep a note of which class (ck) disappeared from the canonicalization.
        // 3. Aggregate based on which class disappeared from the canonicalization. For each ck, the sets of ci that caused ck to
        //    disappear from the canonicalization will be the set of class names that can be canonicalized to ck.
        // The Tailwind canonicalization CLI is the oracle.

        // For the above algorithm to work, we need to de-dupe the class names first.

        var deduped = classNames.Distinct().ToList();

        var initial = await CanonicalizeViaCliAsync(deduped, filePath);

        var disappearedMap = new Dictionary<string, List<string>>();

        for (int i = 0; i < deduped.Count; i++)
        {
            var canonicalizeMinusI = await CanonicalizeViaCliAsync(
                deduped.Where((_, index) => index != i),
                filePath
            );

            var disappeared = initial.Except(canonicalizeMinusI).FirstOrDefault();

            if (disappeared is null)
            {
                continue;
            }

            if (!disappearedMap.TryGetValue(disappeared, out var group))
            {
                group = [];
                disappearedMap[disappeared] = group;
            }

            group.Add(deduped[i]);
        }

        return disappearedMap.Select(kvp => new Canonicalization(kvp.Value, kvp.Key));
    }

    /// <summary>
    /// Sends one candidate group to the CLI and reads its matching JSON-line response.
    /// </summary>
    /// <param name="classNames">The candidate class names, without embedded line breaks.</param>
    /// <param name="filePath">The file whose project configuration should be used.</param>
    /// <returns>The canonicalized class names for this request.</returns>
    private async Task<string[]> CanonicalizeViaCliAsync(
        IEnumerable<string> classNames,
        string filePath
    )
    {
        var project = await _projectConfigurationManager.GetCompletionConfigurationByFilePathAsync(
            filePath
        );

        var input = string.Join(" ", classNames);

        // Serialize process lookup as well as I/O so concurrent requests cannot create
        // duplicate processes or consume each other's responses.
        var gate = _processGates.GetOrAdd(project, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await _settingsGate.WaitAsync();
            try
            {
                if (_settings is null)
                {
                    await InitializeSettingsAsync();
                }
            }
            finally
            {
                _settingsGate.Release();
            }

            var process = GetActiveProcessFor(project);
            try
            {
                await process.StandardInput.WriteLineAsync(input);
                await process.StandardInput.FlushAsync();

                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
                {
                    if (TryReadResponse(line, input, out var output))
                    {
                        return output.Split(
                            Array.Empty<char>(),
                            StringSplitOptions.RemoveEmptyEntries
                        );
                    }
                }

                throw new InvalidOperationException(
                    "Tailwind CLI closed stdout before returning a matching canonicalization response."
                );
            }
            catch
            {
                // A failed exchange cannot be reused safely: a delayed response could
                // otherwise be consumed by the next request.
                lock (_processLifecycleLock)
                {
                    _processes.TryRemove(project, out _);
                    StopProcess(process);
                    process.Dispose();
                }
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Extracts a canonicalization response while ignoring banners, diagnostics, and other requests' output.
    /// </summary>
    /// <param name="line">The stdout line to inspect.</param>
    /// <param name="input">The exact candidate group sent to the CLI.</param>
    /// <param name="output">The canonicalized candidate group when a matching response is found.</param>
    /// <returns>Whether the line is a valid JSON response for the current request.</returns>
    private static bool TryReadResponse(string line, string input, out string output)
    {
        output = "";
        if (!line.TrimStart().StartsWith("{"))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("input", out var responseInput)
                || responseInput.ValueKind != JsonValueKind.String
                || !string.Equals(responseInput.GetString(), input, StringComparison.Ordinal)
                || !root.TryGetProperty("output", out var responseOutput)
                || responseOutput.ValueKind != JsonValueKind.String
            )
            {
                return false;
            }

            output = responseOutput.GetString()!;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the active process for the given project. If there is no active process, it starts a new one and returns it.
    /// </summary>
    /// <param name="project">The project for which to get the active process.</param>
    /// <returns>The active process for the given project.</returns>
    private Process GetActiveProcessFor(ProjectCompletionValues project)
    {
        lock (_processLifecycleLock)
        {
            if (_disposed)
            {
                // Initialization may have completed while disposal was in progress.
                base.Dispose();
                throw new ObjectDisposedException(nameof(Canonicalizer));
            }

            if (_processes.TryGetValue(project, out var process))
            {
                if (IsProcessActive(process))
                {
                    return process;
                }

                _processes.TryRemove(project, out _);
                process.Dispose();
            }

            var startInfo = GetCliProcessStartInfo(
                project,
                $"canonicalize --stream --format jsonl --css \"{project.FilePath}\""
            );
            // /c avoids the interactive shell banner; /d disables AutoRun startup
            // scripts, and /q disables command echoing.
            startInfo.Arguments = "/d /q " + startInfo.Arguments;

            var newProcess = StartProcess(startInfo);
            // Keep stderr drained independently. PostSetupProcess cannot be used here,
            // because it would also consume stdout needed by the request/response reader.
            newProcess.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data is not null)
                {
                    OnCliMessage(e.Data);
                }
            };
            newProcess.BeginErrorReadLine();
            _processes[project] = newProcess;
            return newProcess;
        }
    }

    /// <summary>
    /// Stops and disposes canonicalization processes and removes the settings subscription.
    /// </summary>
    public override void Dispose()
    {
        lock (_processLifecycleLock)
        {
            _disposed = true;
            base.Dispose();
            foreach (var process in _processes.Values)
            {
                StopProcess(process);
                process.Dispose();
            }
            _processes.Clear();
        }
    }

    /// <summary>
    /// Represents a cache entry.
    /// </summary>
    /// <remarks>
    /// Let's imagine we have a set of classes: {"px-2", "text-[#fff]", "py-2"}. The outer cache should contain a key "px-2" with
    /// <see cref="Canonicalization"/> value containing ClassNames = ["px-2", "py-2"] and CanonicalizedClassName = "p-2". It should
    /// also contain a key "text-[#fff]" with ClassNames = ["text-[#fff]"] and CanonicalizedClassNames = "text-white".
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="Canonicalization"/> class with a set of class names and their canonicalized versions.
    /// </remarks>
    /// <param name="classNames">A set of matching class names</param>
    /// <param name="canonicalized">The canonicalization of those class names</param>
    public class Canonicalization(IEnumerable<string> classNames, string canonicalized)
    {
        /// <summary>
        /// The set of matching class names.
        /// </summary>
        public string[] ClassNames { get; } = [.. classNames.OrderBy(s => s)];

        /// <summary>
        /// The canonicalization of the class names.
        /// </summary>
        public string CanonicalizedClassName { get; } = canonicalized;
    }
}
