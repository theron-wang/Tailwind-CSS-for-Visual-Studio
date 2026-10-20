using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class ConfigurationClassGeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomThemeDescriptions_UseGeneratedNamesAndSurvivePluginLoading(bool extend)
    {
        var original = new ProjectCompletionValues
        {
            Version = TailwindVersion.V3,
            ConfigurationValueToClassStems = new()
            {
                ["fontSize"] = ["text-{*}"],
                ["opacity"] = ["opacity-{*}"],
            },
            Classes = [new() { Name = "text-base" }, new() { Name = "opacity-100" }],
            DescriptionMapper = new()
            {
                ["text-base"] = "font-size: 1rem;",
                ["opacity-100"] = "opacity: 1;",
            },
        };
        var config = new TailwindConfiguration
        {
            PluginDescriptions = new() { ["plugin"] = "display: block;" },
        };
        var values = extend ? config.ExtendedValues : config.OverridenValues;
        values["fontSize"] = new Dictionary<string, object>
        {
            ["tiny"] = "0.5rem",
            ["big"] = "3rem",
        };
        values["opacity"] = new Dictionary<string, object>
        {
            ["DEFAULT"] = "0.75",
            ["-5"] = "-0.05",
        };
        var project = original.Copy();
        await Loader(original).LoadForTest(project, config);
        Assert.Equal("font-size: 0.5rem;", project.CustomDescriptionMapper["text-tiny"]);
        Assert.Equal("font-size: 3rem;", project.CustomDescriptionMapper["text-big"]);
        Assert.Equal("opacity: 0.75;", project.CustomDescriptionMapper["opacity"]);
        Assert.Equal("opacity: -0.05;", project.CustomDescriptionMapper["-opacity-5"]);
        Assert.Equal("display: block;", project.CustomDescriptionMapper["plugin"]);
    }

    [Fact]
    public async Task ExtendingStemSpecificColorsAndSpacing_PreservesOverrides()
    {
        var original = new ProjectCompletionValues
        {
            Version = TailwindVersion.V3,
            ConfigurationValueToClassStems = new()
            {
                ["padding"] = ["p-{s}"],
                ["backgroundColor"] = ["bg-{c}"],
            },
            SpacingMapper = new() { ["default"] = "1rem" },
            ColorMapper = new() { ["default"] = "255,0,0" },
        };
        var config = new TailwindConfiguration
        {
            OverridenValues = new()
            {
                ["padding"] = new Dictionary<string, object> { ["override"] = "2rem" },
                ["backgroundColor"] = new Dictionary<string, object>
                {
                    ["override"] = "rgb(0 255 0)",
                },
            },
            ExtendedValues = new()
            {
                ["padding"] = new Dictionary<string, object> { ["extend"] = "3rem" },
                ["backgroundColor"] = new Dictionary<string, object>
                {
                    ["extend"] = "rgb(0 0 255)",
                },
            },
        };
        var project = original.Copy();
        await Loader(original).LoadForTest(project, config);
        Assert.Equal(["extend", "override"], project.CustomSpacingMappers["p-{0}"].Keys.Order());
        Assert.Equal(["extend", "override"], project.CustomColorMappers["bg-{0}"].Keys.Order());
        Assert.Equal("2rem", project.CustomSpacingMappers["p-{0}"]["override"]);
        Assert.Equal("0,255,0", project.CustomColorMappers["bg-{0}"]["override"]);
    }

    [Theory]
    [InlineData("color:red;tab-size:--value(integer);", "color:red;tab-size:{0};")]
    [InlineData(
        "@media (width > 1px){tab-size:--value(integer);}",
        "@media (width > 1px){tab-size:{0};}"
    )]
    public async Task FunctionalUtility_PreservesDeclarationsAndNestedBraces(
        string input,
        string expected
    )
    {
        var project = new ProjectCompletionValues { Version = TailwindVersion.V4 };
        await Loader(project)
            .LoadForTest(project, new() { PluginDescriptions = new() { ["tab-*"] = input } });
        Assert.Equal(expected, project.CustomDescriptionMapper["tab-{n}"]);
    }

    [Fact]
    public async Task CustomVariant_ReplacesBuiltInDescription()
    {
        var original = new ProjectCompletionValues
        {
            Version = TailwindVersion.V4,
            VariantsToDescriptions = new()
            {
                ["dark"] = "@media (prefers-color-scheme: dark) { {0} }",
            },
        };
        var project = original.Copy();
        await Loader(original)
            .LoadForTest(
                project,
                new()
                {
                    PluginVariantDescriptions = new()
                    {
                        ["dark"] = "&:where(.dark, .dark *) { @slot; }",
                    },
                }
            );
        Assert.Equal("&:where(.dark, .dark *) { {0} }", project.VariantsToDescriptions["dark"]);
    }

    [Fact]
    public async Task MalformedRgb_DoesNotAbortOtherThemeValues()
    {
        var original = new ProjectCompletionValues { Version = TailwindVersion.V3 };
        var project = original.Copy();
        await Loader(original)
            .LoadForTest(
                project,
                new()
                {
                    ExtendedValues = new()
                    {
                        ["colors"] = new Dictionary<string, object>
                        {
                            ["invalid"] = "rgb",
                            ["valid"] = "rgb(1 2 3)",
                        },
                    },
                }
            );
        Assert.Equal("1,2,3", project.ColorMapper["valid"]);
        Assert.Equal("{noparse}rgb", project.ColorMapper["invalid"]);
    }

    private static CompletionConfiguration Loader(ProjectCompletionValues original) =>
        new() { ProjectConfigurationInitializer = new() { Original = original } };
}
