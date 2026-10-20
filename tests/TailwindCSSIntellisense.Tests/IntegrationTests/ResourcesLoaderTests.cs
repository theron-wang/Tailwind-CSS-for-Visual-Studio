using System.Globalization;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Initialization;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

public class ResourcesLoaderTests
{
    [Theory]
    [InlineData(TailwindVersion.V3)]
    [InlineData(TailwindVersion.V4)]
    [InlineData(TailwindVersion.V4_1)]
    [InlineData(TailwindVersion.V4_2)]
    public async Task BundledResources_LoadWithCultureIndependentCssValues(TailwindVersion version)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var result = await ResourcesLoader.LoadResourcesForVersionAsync(version);
            Assert.Equal("0.125rem", result.UnsetProject.SpacingMapper["0.5"]);
            Assert.NotEmpty(result.ClassTypes);
            Assert.NotEmpty(result.UnsetProject.DescriptionMapper);
            Assert.NotEmpty(await ResourcesLoader.LoadOrderForVersionAsync(version));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
