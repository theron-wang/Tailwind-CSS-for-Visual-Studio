using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;

namespace TailwindCSSIntellisense.Initialization;

/// <summary>
/// For tests, see https://github.com/theron-wang/Tailwind-Class-Generator/blob/master/tests/AllTailwindClassesGenerator.Tests/V4DiffRevertTests.cs
/// </summary>
internal static class ResourcesLoader
{
    /// <summary>
    /// Represents the result of an operation, containing a collection of class types, unset project completion values,
    /// and opacity values.
    /// </summary>
    /// <param name="classTypes">The list of class types included in the result. Cannot be null.</param>
    /// <param name="unsetProject">The unset project completion values associated with the result.</param>
    /// <param name="opacity">The list of opacity values included in the result. Cannot be null.</param>
    public class Result(
        List<ClassTypeBase> classTypes,
        UnsetProjectCompletionValues unsetProject,
        List<int> opacity
    )
    {
        public List<ClassTypeBase> ClassTypes { get; } = classTypes;
        public UnsetProjectCompletionValues UnsetProject { get; } = unsetProject;
        public List<int> Opacity { get; } = opacity;
    }

    /// <summary>
    /// Asynchronously loads the required resources for the specified Tailwind CSS version.
    /// </summary>
    /// <param name="version">The Tailwind CSS version for which to load resources. Determines whether major or minor version resources are
    /// loaded.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="Result"/> indicating
    /// the outcome of the resource loading operation.</returns>
    public static async Task<Result> LoadResourcesForVersionAsync(TailwindVersion version)
    {
        var isMajorVersion = version == version.GetMajorVersion();

        return isMajorVersion
            ? await LoadMajorVersionAsync(version)
            : await LoadMinorVersionAsync(version);
    }

    /// <summary>
    /// Asynchronously loads the CSS class order for the specified Tailwind CSS version.
    /// </summary>
    /// <remarks>If a version-specific order file is present, the method merges it with the major version's
    /// base order. Otherwise, it loads the order from the major version's file. This method is typically used to ensure
    /// consistent class ordering when processing or generating Tailwind CSS.</remarks>
    /// <param name="version">The Tailwind CSS version for which to retrieve the class order.</param>
    /// <param name="forVariants">true to load the order for variant classes; otherwise, false to load the base class order. The default is false.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of class names in the order
    /// defined for the specified version. The list will be empty if no order information is available.</returns>
    public static async Task<List<string>> LoadOrderForVersionAsync(
        TailwindVersion version,
        bool forVariants = false
    )
    {
        var baseFolder = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "Resources"
        );
        var fileName = forVariants ? "variantorder.json" : "order.json";
        var majorVersionFolder = Path.Combine(baseFolder, version.GetMajorVersion().ToString());
        var majorVersionFile = Path.Combine(majorVersionFolder, fileName);
        var minorVersionFile = Path.Combine(majorVersionFolder, version.ToString(), fileName);

        if (File.Exists(minorVersionFile))
        {
            return await RevertOrderDiffAsync(majorVersionFile, minorVersionFile);
        }

        return await LoadJsonAsync<List<string>>(majorVersionFile);
    }

    /// <summary>
    /// Asynchronously loads Tailwind CSS major version resources and constructs a result containing class variants,
    /// project metadata, and opacity values.
    /// </summary>
    /// <remarks>If the specified version is TailwindVersion.V4 or greater, additional theme and variant
    /// description resources are loaded. Throws if required resource files are missing or invalid.</remarks>
    /// <param name="version">The Tailwind CSS major version to load resources for. Must correspond to a supported version directory.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a Result object with loaded class
    /// variants, project metadata, and opacity values for the specified version.</returns>
    private static async Task<Result> LoadMajorVersionAsync(TailwindVersion version)
    {
        var baseFolder = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "Resources"
        );
        var versionFolder = Path.Combine(baseFolder, version.ToString());

        var project = new UnsetProjectCompletionValues { Version = version };

        List<ClassTypeBase> classTypes = [];
        List<int> opacity = [];

        var loadTasks = new List<Task>
        {
            LoadJsonAsync<Dictionary<string, string>>(
                Path.Combine(versionFolder, "colors.json"),
                c => project.ColorMapper = c
            ),
            LoadJsonAsync<List<string>>(
                Path.Combine(baseFolder, "spacing.json"),
                spacing =>
                {
                    project.SpacingMapper = [];
                    foreach (var s in spacing)
                    {
                        project.SpacingMapper[s] =
                            s == "px"
                                ? "1px"
                                : $"{float.Parse(s, CultureInfo.InvariantCulture) / 4}rem";
                    }
                }
            ),
            LoadJsonAsync<List<int>>(Path.Combine(baseFolder, "opacity.json"), o => opacity = o),
            LoadJsonAsync<Dictionary<string, List<string>>>(
                Path.Combine(baseFolder, "tailwindconfig.json"),
                c => project.ConfigurationValueToClassStems = c
            ),
            LoadJsonAsync<Dictionary<string, string>>(
                Path.Combine(versionFolder, "descriptions.json"),
                d => project.DescriptionMapper = d
            ),
        };

        if (version >= TailwindVersion.V4)
        {
            loadTasks.Add(
                LoadJsonAsync<List<ClassType>>(
                    Path.Combine(versionFolder, "classes.json"),
                    v => classTypes = [.. v.Cast<ClassTypeBase>()]
                )
            );
            loadTasks.Add(
                LoadJsonAsync<Dictionary<string, string>>(
                    Path.Combine(versionFolder, "theme.json"),
                    d => project.CssVariables = d
                )
            );
            loadTasks.Add(
                LoadJsonAsync<Dictionary<string, string>>(
                    Path.Combine(versionFolder, "variants.json"),
                    d => project.VariantsToDescriptions = d
                )
            );
        }
        else
        {
            loadTasks.Add(
                LoadJsonAsync<List<ClassTypeV3>>(
                    Path.Combine(versionFolder, "classes.json"),
                    v => classTypes = [.. v.Cast<ClassTypeBase>()]
                )
            );
            loadTasks.Add(
                LoadJsonAsync<List<string>>(
                    Path.Combine(versionFolder, "variants.json"),
                    m => project.Variants = m
                )
            );
        }

        await Task.WhenAll(loadTasks);

        return new Result(classTypes, project, opacity);
    }

    /// <summary>
    /// Asynchronously loads Tailwind CSS minor version resources and constructs a result containing class variants,
    /// project configuration, and opacity values.
    /// </summary>
    /// <remarks>This method aggregates data from multiple resource files corresponding to the specified
    /// Tailwind CSS minor version. It loads configuration, class variants, color mappings, and other related resources.
    /// If the minor version is greater than or equal to version 4, additional theme and variant description resources
    /// are loaded.</remarks>
    /// <param name="minorVersion">The Tailwind CSS minor version to load. Must specify a valid version supported by the resource files.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a Result object with loaded class
    /// variants, project configuration, and opacity values for the specified minor version.</returns>
    private static async Task<Result> LoadMinorVersionAsync(TailwindVersion minorVersion)
    {
        var baseFolder = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "Resources"
        );
        var majorVersionFolder = Path.Combine(
            baseFolder,
            minorVersion.GetMajorVersion().ToString()
        );
        var minorVersionFolder = Path.Combine(majorVersionFolder, minorVersion.ToString());

        var project = new UnsetProjectCompletionValues { Version = minorVersion };

        List<ClassTypeBase> classTypes = [];
        List<int> opacity = [];

        var loadTasks = new List<Task>
        {
            LoadJsonAsync<List<string>>(
                Path.Combine(baseFolder, "spacing.json"),
                spacing =>
                {
                    project.SpacingMapper = [];
                    foreach (var s in spacing)
                    {
                        project.SpacingMapper[s] =
                            s == "px"
                                ? "1px"
                                : $"{float.Parse(s, CultureInfo.InvariantCulture) / 4}rem";
                    }
                }
            ),
            LoadJsonAsync<List<int>>(Path.Combine(baseFolder, "opacity.json"), o => opacity = o),
            LoadJsonAsync<Dictionary<string, List<string>>>(
                Path.Combine(baseFolder, "tailwindconfig.json"),
                c => project.ConfigurationValueToClassStems = c
            ),
        };

        if (File.Exists(Path.Combine(minorVersionFolder, "classes.json")))
        {
            loadTasks.Add(
                RevertKeyedArrayDiffAsync(
                        Path.Combine(majorVersionFolder, "classes.json"),
                        Path.Combine(minorVersionFolder, "classes.json"),
                        "s"
                    )
                    .ThenAsync(result =>
                    {
                        if (minorVersion >= TailwindVersion.V4)
                        {
                            classTypes = [.. result.Select(obj => obj.Deserialize<ClassType>()!)];
                            return;
                        }
                        classTypes = [.. result.Select(obj => obj.Deserialize<ClassTypeV3>()!)];
                    })
            );
        }
        else
        {
            if (minorVersion >= TailwindVersion.V4)
            {
                loadTasks.Add(
                    LoadJsonAsync<List<ClassType>>(
                        Path.Combine(majorVersionFolder, "classes.json"),
                        v => classTypes = [.. v.Cast<ClassTypeBase>()]
                    )
                );
            }
            else
            {
                loadTasks.Add(
                    LoadJsonAsync<List<ClassTypeV3>>(
                        Path.Combine(majorVersionFolder, "classes.json"),
                        v => classTypes = [.. v.Cast<ClassTypeBase>()]
                    )
                );
            }
        }

        loadTasks.Add(
            LoadVersionedJsonAsync<Dictionary<string, string>>(
                majorVersionFolder,
                minorVersionFolder,
                "colors.json",
                c => project.ColorMapper = c
            )
        );

        loadTasks.Add(
            LoadVersionedJsonAsync<Dictionary<string, string>>(
                majorVersionFolder,
                minorVersionFolder,
                "descriptions.json",
                d => project.DescriptionMapper = d
            )
        );

        if (minorVersion >= TailwindVersion.V4)
        {
            loadTasks.Add(
                LoadVersionedJsonAsync<Dictionary<string, string>>(
                    majorVersionFolder,
                    minorVersionFolder,
                    "theme.json",
                    t => project.CssVariables = t
                )
            );

            loadTasks.Add(
                LoadVersionedJsonAsync<Dictionary<string, string>>(
                    majorVersionFolder,
                    minorVersionFolder,
                    "variants.json",
                    v => project.VariantsToDescriptions = v
                )
            );
        }
        else
        {
            loadTasks.Add(
                LoadVersionedJsonAsync<List<string>>(
                    majorVersionFolder,
                    minorVersionFolder,
                    "variants.json",
                    v => project.Variants = v
                )
            );
        }

        await Task.WhenAll(loadTasks);

        return new Result(classTypes, project, opacity);
    }

    /// <summary>
    /// Asynchronously loads a JSON file for the specified version, applying any available minor version diff, and
    /// assigns the deserialized object to the provided action.
    /// </summary>
    /// <remarks>If a minor version diff file exists, it is applied to the major version file before
    /// deserialization. Otherwise, only the major version file is loaded.</remarks>
    /// <typeparam name="T">The type into which the JSON content is deserialized.</typeparam>
    /// <param name="majorVersionFolder">The path to the folder containing the major version of the JSON file.</param>
    /// <param name="minorVersionFolder">The path to the folder containing the minor version diff of the JSON file.</param>
    /// <param name="fileName">The name of the JSON file to load.</param>
    /// <param name="assign">An action to receive the deserialized object of type T.</param>
    /// <returns>A task that represents the asynchronous load operation.</returns>
    private static Task LoadVersionedJsonAsync<T>(
        string majorVersionFolder,
        string minorVersionFolder,
        string fileName,
        Action<T> assign
    )
    {
        var minorPath = Path.Combine(minorVersionFolder, fileName);
        var majorPath = Path.Combine(majorVersionFolder, fileName);

        if (File.Exists(minorPath))
        {
            return RevertObjectDiffAsync(majorPath, minorPath)
                .ThenAsync(result =>
                {
                    assign(result.Deserialize<T>()!);
                });
        }

        return LoadJsonAsync<T>(majorPath, assign);
    }

    /// <summary>
    /// Reverts changes described by a diff file to a JSON object loaded from the specified original path.
    /// </summary>
    /// <remarks>The method removes properties specified in the diff's 'remove' section and restores or
    /// overrides properties from the 'add' and 'override' sections. The returned object reflects the state after
    /// applying the reverse of the diff.</remarks>
    /// <param name="originalPath">The file path to the original JSON object. Must not be null or empty.</param>
    /// <param name="diffPath">The file path to the JSON diff file describing changes to revert. Must not be null or empty.</param>
    /// <returns>A JsonObject representing the original object with the diff changes reverted.</returns>
    private static async Task<JsonObject> RevertObjectDiffAsync(
        string originalPath,
        string diffPath
    )
    {
        var original = await LoadJsonAsync<JsonObject>(originalPath);
        var diff = await LoadJsonAsync<JsonObject>(diffPath);

        var result = original;
        var add = diff["add"] as JsonObject ?? [];
        var remove = diff["remove"] as JsonArray ?? [];

        foreach (var key in remove)
        {
            result.Remove(key!.ToString());
        }

        foreach (var pair in add.ToList())
        {
            DetachJsonNode(pair.Value!);
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    /// <summary>
    /// Reverts a keyed array diff by applying 'add', 'remove', and 'override' operations from a diff file to an
    /// original JSON array file, producing the resulting collection.
    /// </summary>
    /// <remarks>The method expects both the original and diff files to be valid JSON. If the diff file
    /// contains 'add' or 'override' entries with duplicate keys, the last occurrence takes precedence. The returned
    /// collection does not modify the input files.</remarks>
    /// <param name="originalPath">The file path to the original JSON array. Must not be null or empty.</param>
    /// <param name="diffPath">The file path to the JSON diff file containing 'add', 'remove', and 'override' arrays. Must not be null or
    /// empty.</param>
    /// <param name="keyProperty">The property name used as the unique key for matching objects within the arrays. Must not be null or empty.</param>
    /// <returns>An ordered collection of JsonObject instances representing the original array after the diff has been reverted.
    /// The collection is ordered by the key property in ordinal order.</returns>
    private static async Task<IEnumerable<JsonObject>> RevertKeyedArrayDiffAsync(
        string originalPath,
        string diffPath,
        string keyProperty
    )
    {
        var original = await LoadJsonAsync<JsonArray>(originalPath);
        var diff = await LoadJsonAsync<JsonObject>(diffPath);

        var originalByKey = ToKeyedDictionary(original, keyProperty);

        var add = diff["add"] as JsonArray ?? [];
        var remove = diff["remove"] as JsonArray ?? [];

        foreach (var key in remove)
        {
            var keyAsString = key!.ToString();

            if (!string.IsNullOrWhiteSpace(keyAsString))
            {
                originalByKey.Remove(keyAsString);
            }
        }

        foreach (var node in add.ToList())
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            var key = obj[keyProperty]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(key))
            {
                DetachJsonNode(obj);
                originalByKey[key!] = obj;
            }
        }

        return originalByKey
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value);
    }

    /// <summary>
    /// Reverts the changes described by a diff file to restore the original order of items from a JSON file.
    /// </summary>
    /// <remarks>The diff file must contain 'add' and 'remove' arrays describing which items to insert and
    /// remove. This method does not modify the files on disk; it only returns the reverted list in memory.</remarks>
    /// <param name="originalPath">The path to the JSON file containing the original list of items. Cannot be null or empty.</param>
    /// <param name="diffPath">The path to the JSON file containing the diff describing additions and removals. Cannot be null or empty.</param>
    /// <returns>A list of strings representing the items in their original order after reverting the diff. The list will be
    /// empty if the original file contains no items.</returns>
    private static async Task<List<string>> RevertOrderDiffAsync(
        string originalPath,
        string diffPath
    )
    {
        var result = await LoadJsonAsync<List<string>>(originalPath);
        var diff = await LoadJsonAsync<JsonObject>(diffPath);

        var additions = diff["add"] as JsonArray ?? [];
        var remove = diff["remove"] as JsonArray ?? [];

        foreach (var toRemove in remove)
        {
            result.Remove(toRemove!.ToString());
        }

        foreach (var toAdd in additions)
        {
            // Each toAdd is {"className": index}
            var obj = toAdd!.AsObject();
            var key = obj.First().Key;

            obj.TryGetPropertyValue(key, out var value);

            result.Insert(value!.GetValue<int>(), key);
        }

        return result;
    }

    private static Dictionary<string, JsonObject> ToKeyedDictionary(
        JsonArray array,
        string keyProperty
    )
    {
        Dictionary<string, JsonObject> result = [];

        foreach (var node in array)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidDataException("Expected a JSON object entry.");
            }

            var key =
                obj[keyProperty]?.GetValue<string>()
                ?? throw new InvalidDataException(
                    $"Expected property '{keyProperty}' to be present."
                );

            result[key] = obj;
        }

        return result;
    }

    private static async Task<T> LoadJsonAsync<T>(string path)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (await JsonSerializer.DeserializeAsync<T>(fs))!;
    }

    private static async Task LoadJsonAsync<T>(string path, Action<T> process)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var data = await JsonSerializer.DeserializeAsync<T>(fs);
        process(data!);
    }

    private static void DetachJsonNode(JsonNode node)
    {
        var parent = node.Parent;

        if (parent is JsonObject obj)
        {
            obj.Remove(obj.First(pair => pair.Value == node).Key);
        }
        else if (parent is JsonArray array)
        {
            array.Remove(node);
        }
    }
}

file static class TaskThenExtension
{
    [SuppressMessage(
        "Usage",
        "VSTHRD003:Avoid awaiting foreign Tasks",
        Justification = "Not intended to be used where deadlocks are possible"
    )]
    public static async Task ThenAsync<T>(this Task<T> task, Action<T> process)
    {
        process(await task);
    }
}
