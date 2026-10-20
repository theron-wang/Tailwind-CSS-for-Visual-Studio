using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Completions.Sources;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class ClassCompletionGeneratorTests
{
    [Theory]
    [InlineData(TailwindVersion.V3, "tw-", "", "tw-block", "tw-plugin", "hover:")]
    [InlineData(
        TailwindVersion.V3,
        "tw-",
        "focus:",
        "focus:tw-block",
        "focus:tw-plugin",
        "focus:hover:"
    )]
    [InlineData(
        TailwindVersion.V3,
        "tw-",
        "focus:!tw-b",
        "focus:!tw-block",
        "focus:!tw-plugin",
        "focus:hover:"
    )]
    [InlineData(TailwindVersion.V4, "tw:", "", "tw:block", "tw:plugin", "tw:hover:")]
    [InlineData(
        TailwindVersion.V4,
        "tw:",
        "tw:focus:",
        "tw:focus:block",
        "tw:focus:plugin",
        "tw:focus:hover:"
    )]
    public void PrefixAndImportantModifier_AppearOnceInCorrectPosition(
        TailwindVersion version,
        string prefix,
        string input,
        string utility,
        string plugin,
        string variant
    )
    {
        var values = new ProjectCompletionValues
        {
            Version = version,
            Prefix = prefix,
            Classes = [new() { Name = "block" }],
            PluginClasses = ["plugin"],
            Variants = ["hover"],
        };
        using var descriptions = new DescriptionGenerator(new SettingsProvider());
        using var generator = new Probe(values, descriptions);
        var completions = generator.Complete(input);
        Assert.Contains(completions, c => c.InsertionText == utility);
        Assert.Contains(completions, c => c.InsertionText == plugin);
        Assert.Contains(completions, c => c.InsertionText == variant);
        Assert.DoesNotContain(completions, c => c.InsertionText.Contains(prefix + prefix));
    }

    [Theory]
    [InlineData("text-sm", "text-sm/tight")]
    [InlineData("bg-linear-to-r", "bg-linear-to-r/srgb")]
    public void ModifierCompletions_RespectBlocklist(string name, string blocked)
    {
        var values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V4,
            Classes = [new() { Name = name }],
            Blocklist = [blocked],
        };
        values.CssVariables = new()
        {
            ["--text-sm"] = "0.875rem",
            ["--leading-tight"] = "1.25",
            ["--leading-normal"] = "1.5",
        };
        using var descriptions = new DescriptionGenerator(new SettingsProvider());
        using var generator = new Probe(values, descriptions);
        var completions = generator.Complete(name + "/");
        Assert.DoesNotContain(completions, c => c.InsertionText == blocked);
        Assert.Contains(
            completions,
            c => c.InsertionText.StartsWith(name + "/") && c.InsertionText != name + "/[]"
        );
    }

    [Fact]
    public void ArbitraryVariant_PreservesInternalColon()
    {
        var values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V4,
            Classes = [new() { Name = "block" }],
        };
        using var descriptions = new DescriptionGenerator(new SettingsProvider());
        using var generator = new Probe(values, descriptions);
        Assert.Contains(
            generator.Complete("[&:is(:hover,:focus)]:b"),
            c => c.InsertionText == "[&:is(:hover,:focus)]:block"
        );
    }

    private sealed class Probe : ClassCompletionGenerator
    {
        public Probe(ProjectCompletionValues values, DescriptionGenerator descriptions)
            : base(
                new FakeTextBuffer(""),
                new ProjectConfigurationManager(),
                new ColorIconGenerator(),
                descriptions,
                new SettingsProvider(),
                new CompletionConfiguration(),
                new ProjectConfigurationInitializer()
            )
        {
            _projectCompletionValues = values;
        }

        public List<Completion> Complete(string input) => GetCompletions(input);
    }
}
