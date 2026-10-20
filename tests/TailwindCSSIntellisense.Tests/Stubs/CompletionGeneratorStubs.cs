using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration.Descriptions;

namespace Microsoft.VisualStudio.Language.Intellisense
{
    public sealed class Completion(
        string displayText,
        string insertionText,
        string? description,
        object? iconSource,
        string? iconAutomationText
    )
    {
        public string DisplayText { get; } = displayText;
        public string InsertionText { get; } = insertionText;
        public string? Description { get; } = description;
        public object? IconSource { get; } = iconSource;
        public string? IconAutomationText { get; } = iconAutomationText;
        public Microsoft.VisualStudio.Text.PropertyCollection Properties { get; } = new();
    }
}

namespace TailwindCSSIntellisense.Completions
{
    public sealed class ProjectConfigurationInitializer
    {
        public ProjectCompletionValues Original { get; set; } = new();
        public Func<Task>? BeforeInitialize { get; set; }
        public Func<Task<ProjectCompletionValues>>? LoadTemplate { get; set; }

        public Task InitializeAsync() => BeforeInitialize?.Invoke() ?? Task.CompletedTask;

        public List<int> Opacity { get; } = [0, 50, 100];

        public Task<ProjectCompletionValues> GetUnsetCompletionConfigurationAsync(
            TailwindVersion version
        ) => LoadTemplate?.Invoke() ?? Task.FromResult(Original);
    }
}

namespace TailwindCSSIntellisense.Helpers
{
    public sealed class ColorIconGenerator
    {
        public void ClearCache(ProjectCompletionValues project) { }

        public object? GetImageFromColor(
            ProjectCompletionValues project,
            string stem,
            string color,
            int opacity
        ) => null;
    }
}

namespace TailwindCSSIntellisense.Configuration
{
    public sealed partial class CompletionConfiguration
    {
        public CompletionConfiguration()
        {
            DescriptionGenerator = new([new FontSizeDescriptionGenerator()]);
            ColorIconGenerator = new();
            ProjectConfigurationInitializer = new();
            Reloader = new();
        }

        internal async Task LoadForTest(
            ProjectCompletionValues project,
            TailwindConfiguration config
        )
        {
            await LoadGlobalConfigurationAsync(project, config);
            await LoadIndividualConfigurationOverrideAsync(project, config);
            LoadIndividualConfigurationExtend(project, config);
            await LoadPluginsAsync(project, config);
        }
    }
}

namespace TailwindCSSIntellisense.Node
{
    internal static class CheckForUpdates
    {
        internal static Task UpdateConfigFileFolderAsync(string path) => Task.CompletedTask;
    }
}
