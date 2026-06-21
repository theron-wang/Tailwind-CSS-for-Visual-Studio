using System.Collections.Generic;
using System.Linq;

namespace TailwindCSSIntellisense.Completions;

public class ProjectCompletionValues
{
    /// <summary>
    /// When set, set to lowercase
    /// </summary>
    public string FilePath { get; set; } = null!;

    /// <summary>
    /// Glob format for v3, base folders for v4.
    /// </summary>
    public List<string> ApplicablePaths { get; set; } = [];

    /// <summary>
    /// V4+ only
    /// </summary>
    public List<string> NotApplicablePaths { get; set; } = [];

    internal bool Initialized { get; set; }
    internal List<TailwindClass> Classes { get; set; } = [];
    internal List<string> Variants { get; set; } = [];

    /// <summary>
    /// V4 and above
    /// </summary>
    internal Dictionary<string, string> VariantsToDescriptions { get; set; } = [];
    internal Dictionary<string, string> Breakpoints { get; set; } = [];
    internal Dictionary<string, string> Containers { get; set; } = [];

    internal string? Prefix { get; set; }

    internal Dictionary<string, string> ColorMapper { get; set; } = [];
    internal Dictionary<string, string> SpacingMapper { get; set; } = [];

    /// <summary>
    /// Removed in V4
    /// </summary>
    internal Dictionary<string, List<string>> ConfigurationValueToClassStems { get; set; } = [];

    /// <summary>
    /// Removed in V4
    /// </summary>
    internal Dictionary<string, Dictionary<string, string>> CustomColorMappers { get; set; } = [];

    /// <summary>
    /// Removed in V4
    /// </summary>
    internal Dictionary<string, Dictionary<string, string>> CustomSpacingMappers { get; set; } = [];

    internal Dictionary<string, string> DescriptionMapper { get; set; } = [];
    internal Dictionary<string, string> CustomDescriptionMapper { get; set; } = [];

    /// <summary>
    /// V4 only
    /// </summary>
    internal Dictionary<string, string> CssVariables { get; set; } = [];

    internal List<string> PluginClasses { get; set; } = [];
    internal List<string> PluginVariants { get; set; } = [];

    internal HashSet<string> Blocklist { get; set; } = [];

    internal TailwindVersion Version { get; set; }

    /// <summary>
    /// Is the class in the blocklist?
    /// </summary>
    /// <param name="className">The class to check</param>
    public bool IsClassAllowed(string className)
    {
        if (Version == TailwindVersion.V3)
        {
            className = className.Trim('!').Split(':').Last();

            if (!string.IsNullOrWhiteSpace(Prefix))
            {
                if (className.StartsWith(Prefix))
                {
                    className = className.Substring(Prefix!.Length);
                }
                else if (className.StartsWith($"-{Prefix}"))
                {
                    className = className.Substring(Prefix!.Length + 1);
                }
            }
        }

        return !Blocklist.Contains(className);
    }

    public ProjectCompletionValues Copy()
    {
        return new ProjectCompletionValues
        {
            Initialized = Initialized,
            Classes = [.. Classes.Select(c => c)],
            Variants = [.. Variants],
            Breakpoints = Breakpoints.ToDictionary(p => p.Key, p => p.Value),
            Prefix = Prefix,
            ColorMapper = new Dictionary<string, string>(ColorMapper),
            SpacingMapper = new Dictionary<string, string>(SpacingMapper),
            ConfigurationValueToClassStems = ConfigurationValueToClassStems.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList()
            ),
            CustomColorMappers = CustomColorMappers.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, string>(kvp.Value)
            ),
            CustomSpacingMappers = CustomSpacingMappers.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, string>(kvp.Value)
            ),
            DescriptionMapper = new Dictionary<string, string>(DescriptionMapper),
            CustomDescriptionMapper = new Dictionary<string, string>(CustomDescriptionMapper),
            PluginClasses = [.. PluginClasses],
            PluginVariants = [.. PluginVariants],
            Blocklist = [.. Blocklist],
            Version = Version,
            CssVariables = CssVariables.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            VariantsToDescriptions = VariantsToDescriptions.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value
            ),
        };
    }
}
