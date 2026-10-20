using System.Reflection;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class DescriptionGeneratorTests
{
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
                (Dictionary<string, string?>)
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
