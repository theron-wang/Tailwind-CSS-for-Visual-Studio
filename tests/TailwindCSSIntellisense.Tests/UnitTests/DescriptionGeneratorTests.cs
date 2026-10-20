using System.Reflection;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class DescriptionGeneratorTests
{
    [Theory]
    [InlineData("hover:text-[length:2px]", "font-size: 2px;")]
    [InlineData("bg-[length:200px_100px]", "background-size: 200px 100px;")]
    [InlineData("bg-[200px]", "background-size: 200px;")]
    [InlineData("bg-[position:10%_20%]", "background-position: 10% 20%;")]
    [InlineData(
        "bg-[url(https://example.com/image.png)]",
        "background-image: url(https://example.com/image.png);"
    )]
    public void ArbitraryValues_PreserveColonsInsideValue(string input, string expected)
    {
        using var generator = new DescriptionGenerator(new SettingsProvider());
        Assert.Equal(
            expected,
            generator.GetDescription(
                input,
                new ProjectCompletionValues { Version = TailwindVersion.V4 },
                shouldFormat: false
            )
        );
    }

    [Fact]
    public void V3ImportantClass_StripsModifierBeforePrefix()
    {
        using var generator = new DescriptionGenerator(new SettingsProvider());
        var values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V3,
            Prefix = "tw-",
            DescriptionMapper = new() { ["block"] = "display: block;" },
        };
        Assert.Equal(
            "display: block;",
            generator.GetDescription("hover:!tw-block", values, shouldFormat: false)
        );
    }

    [Fact]
    public void V3ArbitraryInference_UsesCurrentProjectAndReloadedMappings()
    {
        using var generator = new DescriptionGenerator(new SettingsProvider());
        var first = new ProjectCompletionValues { Version = TailwindVersion.V3 };
        var second = new ProjectCompletionValues
        {
            Version = TailwindVersion.V3,
            DescriptionMapper = new()
            {
                ["legacy-a"] = "width: 1px;",
                ["legacy-b"] = "width: 2px;",
            },
        };
        Assert.Null(generator.GetDescription("legacy-[5px]", first, shouldFormat: false));
        Assert.Equal(
            "width: 5px;",
            generator.GetDescription("legacy-[5px]", second, shouldFormat: false)
        );
        second.DescriptionMapper["legacy-a"] = "height: 1px;";
        second.DescriptionMapper["legacy-b"] = "height: 2px;";
        Assert.Equal(
            "height: 5px;",
            generator.GetDescription("legacy-[5px]", second, shouldFormat: false)
        );
    }

    [Fact]
    public void V3ColorCache_UsesResolvedValueAcrossProjectsOverridesAndReloads()
    {
        using var generator = new DescriptionGenerator(new SettingsProvider());
        var values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V3,
            ColorMapper = new() { ["brand"] = "255,0,0" },
            DescriptionMapper = new() { ["bg-{c}"] = "background-color: {0}; /* {1} */" },
        };
        Assert.Contains(
            "rgb(255 0 0)",
            generator.GetDescription("bg-brand", values, shouldFormat: false)
        );
        var second = values.Copy();
        second.ColorMapper["brand"] = "0,0,255";
        Assert.Contains(
            "rgb(0 0 255)",
            generator.GetDescription("bg-brand", second, shouldFormat: false)
        );
        values.CustomColorMappers["bg-{0}"] = new() { ["brand"] = "0,255,0" };
        Assert.Contains(
            "rgb(0 255 0)",
            generator.GetDescription("bg-brand", values, shouldFormat: false)
        );
        values.CustomColorMappers["bg-{0}"]["brand"] = "1,2";
        Assert.Null(generator.GetDescription("bg-brand", values, shouldFormat: false));
    }

    [Theory]
    [InlineData("custom-brand", "color: var(--color-brand);")]
    [InlineData("custom-[color:var(--brand)]", "color: var(--brand);")]
    [InlineData("custom-(color:--brand)", "color: var(--brand);")]
    public void FunctionalColorUtility_UsesCustomDescription(string className, string expected)
    {
        using var generator = new DescriptionGenerator(new SettingsProvider());
        var values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V4,
            ColorMapper = new() { ["brand"] = "#123456" },
            CustomDescriptionMapper = new() { ["custom-{c}"] = "color: {0};" },
        };
        Assert.Equal(expected, generator.GetDescription(className, values, shouldFormat: false));
    }

    [Fact]
    public void GetDescription_ReusesColorConversionForRepeatedTooltips()
    {
        using var generator = new DescriptionGenerator(new SettingsProvider());
        var values = new ProjectCompletionValues { Version = TailwindVersion.V4 };
        values.ColorMapper["brand"] = "#336699";
        values.DescriptionMapper["bg-brand"] = "background-color: var(--color-brand);";
        var before = ColorHelpers.ConversionCount;

        var first = generator.GetDescription("bg-brand", values);
        var second = generator.GetDescription("bg-brand", values);

        Assert.Contains("#336699", first);
        Assert.Equal(first, second);
        Assert.Equal(before + 1, ColorHelpers.ConversionCount);
    }

    [Theory]
    [InlineData("min-[640px]", "@media (min-width: 640px)")]
    [InlineData("max-[640px]", "@media (max-width: 640px)")]
    public void GetVariantDescription_UsesCorrectBreakpointDirection(
        string variant,
        string expected
    )
    {
        using var generator = new DescriptionGenerator(new SettingsProvider());
        Assert.Equal(
            expected,
            generator.GetVariantDescription(variant, new ProjectCompletionValues())
        );
    }

    [Fact]
    public async Task SettingsChange_ClearsCachesWhenGeneratorIsFirstListener()
    {
        var provider = new SettingsProvider();
        using var generator = new DescriptionGenerator(provider);
        var caches = new[] { "_arbitraryDescriptionCache", "_colorDescriptionMapper" }
            .Select(name =>
                (IDictionary<string, string?>)
                    typeof(DescriptionGenerator)
                        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(generator)!
            )
            .ToArray();
        foreach (var cache in caches)
        {
            cache["old"] = "stale";
        }

        Assert.NotNull(provider.OnSettingsChanged);
        await provider.OnSettingsChanged!(new TailwindSettings());

        Assert.All(caches, cache => Assert.Empty(cache));
    }

    [Fact]
    public void Dispose_RemovesOnlyGeneratorsOwnListener()
    {
        var provider = new SettingsProvider();
        Func<TailwindSettings, Task> existing = _ => Task.CompletedTask;
        provider.OnSettingsChanged = existing;
        using (var generator = new DescriptionGenerator(provider))
        {
            Assert.Equal(2, provider.OnSettingsChanged!.GetInvocationList().Length);
        }
        Assert.Equal(existing, provider.OnSettingsChanged);
    }
}
