using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Build;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

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

    // Canonicalization depends on the entire candidate group, not an individual class.
    // Tasks share concurrent requests; completed entries are never mutated.
    private ConcurrentDictionary<
        ProjectCompletionValues,
        ConcurrentDictionary<string, Task<Canonicalization[]>>
    > _canonicalizationCache = [];

    private readonly Dictionary<ProjectCompletionValues, int> _processVersions = [];
    private int _settingsVersion;
    private ConcurrentDictionary<ProjectCompletionValues, Process> _processes = [];
    private readonly ConcurrentDictionary<ProjectCompletionValues, SemaphoreSlim> _processGates =
    [];
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly object _processLifecycleLock = new();
    private bool _disposed;

    /// <summary>
    /// Given a list of class names, this method returns their canonicalized versions. Classes that are not Tailwind classes or
    /// are already canonicalized are not included in the output list.
    /// </summary>
    /// <param name="classNames">The list of class names to canonicalize.</param>
    /// <param name="filePath">The path to the file containing the class names.</param>
    /// <returns>A list of canonicalized class names.</returns>
    public async Task<List<Canonicalization>> CanonicalizeAsync(
        IEnumerable<string> classNames,
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

        // Materialize once, exclude Razor expressions, and use a stable, unambiguous
        // key so the same group in another order shares the request.
        var candidates = classNames
            .Where(c => !c.Contains(' '))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return [];
        }

        var cache = Volatile
            .Read(ref _canonicalizationCache)
            .GetOrAdd(project, _ => new ConcurrentDictionary<string, Task<Canonicalization[]>>());
        var key = JsonSerializer.Serialize(candidates);
        var completion = new TaskCompletionSource<Canonicalization[]>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var request = cache.GetOrAdd(key, completion.Task);
        if (ReferenceEquals(request, completion.Task))
        {
            try
            {
                completion.SetResult(
                    await FetchCanonicalizationsFromProcessAsync(candidates, filePath)
                );
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }
        try
        {
            return [.. await request];
        }
        catch
        {
            // Failed exchanges must be retried. Remove only this request, so a
            // concurrent retry cannot be evicted by another failing waiter.
            ((ICollection<KeyValuePair<string, Task<Canonicalization[]>>>)cache).Remove(
                new(key, request)
            );
            throw;
        }
    }

    protected override Task SettingsChangedAsync(TailwindSettings settings)
    {
        lock (_processLifecycleLock)
        {
            var updated = base.SettingsChangedAsync(settings);
            _settingsVersion++;
            // In-flight requests retain their old dictionary and cannot repopulate
            // the cache used by requests made after this settings change.
            Interlocked.Exchange(ref _canonicalizationCache, new());
            return updated;
        }
    }

    /// <summary>
    /// Fetches canonicalizations from the Tailwind CLI process for a given set of class names and a file path.
    /// </summary>
    /// <param name="classNames">The set of class names to fetch canonicalizations for.</param>
    /// <param name="filePath">The path to the file containing the class names.</param>
    /// <returns>A list of canonicalizations.</returns>
    private async Task<Canonicalization[]> FetchCanonicalizationsFromProcessAsync(
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
            var excludedIndex = i;
            var canonicalizeMinusI = await CanonicalizeViaCliAsync(
                deduped.Where((_, index) => index != excludedIndex),
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

        // Only return complete, non-overlapping replacements. Selecting against the
        // remaining classes prevents one suggestion from consuming another's input.
        var available = new HashSet<string>(deduped, StringComparer.Ordinal);
        var result = new List<Canonicalization>();
        foreach (
            var canonicalization in disappearedMap
                .Select(kvp => new Canonicalization(kvp.Value, kvp.Key))
                .OrderByDescending(c => c.ClassNames.Length)
        )
        {
            if (
                canonicalization.ClassNames.All(available.Contains)
                && (
                    canonicalization.ClassNames.Length > 1
                    || canonicalization.CanonicalizedClassName != canonicalization.ClassNames[0]
                )
            )
            {
                result.Add(canonicalization);
                available.ExceptWith(canonicalization.ClassNames);
            }
        }
        return [.. result];
    }

    /// <summary>
    /// Sends one candidate group to the CLI and reads its matching JSON-line response.
    /// </summary>
    /// <param name="classNames">The candidate class names, without embedded line breaks.</param>
    /// <param name="filePath">The file whose project configuration should be used.</param>
    /// <returns>The canonicalized class names for this request.</returns>
    protected virtual async Task<string[]> CanonicalizeViaCliAsync(
        IEnumerable<string> classNames,
        string filePath
    )
    {
        var project = await _projectConfigurationManager.GetCompletionConfigurationByFilePathAsync(
            filePath
        );

        // An empty project.FilePath will cause the CLI to close
        if (string.IsNullOrWhiteSpace(project.FilePath))
        {
            return [.. classNames];
        }

        var input = string.Join(" ", classNames);

        if (input.Contains('\r') || input.Contains('\n'))
        {
            throw new ArgumentException(
                "Canonicalization candidates cannot contain line breaks.",
                nameof(classNames)
            );
        }

        // Serialize process lookup as well as I/O so concurrent requests cannot create
        // duplicate processes or consume each other's responses.
        var gate = _processGates.GetOrAdd(project, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await _settingsGate.WaitAsync();
            try
            {
                if (!AreSettingsInitialized)
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

                var deadline = Task.Delay(TimeSpan.FromSeconds(10));
                while (true)
                {
                    var read = process.StandardOutput.ReadLineAsync();
                    if (await Task.WhenAny(read, deadline) != read)
                    {
                        throw new TimeoutException(
                            "Tailwind CLI did not return a canonicalization response in time."
                        );
                    }
                    var line = await read;
                    if (line is null)
                    {
                        break;
                    }
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
                if (
                    IsProcessActive(process)
                    && _processVersions.TryGetValue(project, out var version)
                    && version == _settingsVersion
                )
                {
                    return process;
                }

                _processes.TryRemove(project, out _);
                StopProcess(process);
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
            newProcess.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    OnCliMessage(e.Data);
                }
            };
            newProcess.BeginErrorReadLine();
            _processes[project] = newProcess;
            _processVersions[project] = _settingsVersion;
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
            _processVersions.Clear();
            _canonicalizationCache.Clear();
        }
    }

    /// <summary>
    /// Represents a cache entry.
    /// </summary>
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
