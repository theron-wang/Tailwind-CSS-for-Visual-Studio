using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class ConfigFileParserTests
{
    [Fact]
    public async Task GetConfigurationAsync_ParsesCssSourcesPrefixUtilitiesAndVariants()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"tailwind-config-tests-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            var cssPath = Path.Combine(tempRoot, "app.css");
            await File.WriteAllTextAsync(
                cssPath,
                """
                @import "tailwindcss" source("./src") prefix(tw-);
                @source "./views";
                @custom-variant pointer-coarse (@media (pointer: coarse));
                @utility content-auto {
                  content-visibility: auto;
                }
                @theme {
                  --color-brand-500: #336699;
                }
                """
            );

            var config = await ConfigFileParser.GetConfigurationAsync(
                cssPath,
                TailwindVersion.V4_1
            );

            Assert.Equal("tw-", config.Prefix);
            Assert.Contains(Path.Combine(tempRoot, "src"), config.ContentPaths);
            Assert.Contains(Path.Combine(tempRoot, "views"), config.ContentPaths);
            Assert.Contains("content-auto", config.PluginClasses);
            Assert.Contains("pointer-coarse", config.PluginVariants);
            Assert.True(config.ExtendedValues.ContainsKey("colors"));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetConfigurationAsync_ParsesInlineSourceBlocklistForV41AndAbove()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"tailwind-config-tests-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            var cssPath = Path.Combine(tempRoot, "inline.css");
            await File.WriteAllTextAsync(
                cssPath,
                """
                @import "tailwindcss";
                @source inline("{hover:,focus:,}bg-red-{50,{100..300..100}}");
                """
            );

            var config = await ConfigFileParser.GetConfigurationAsync(
                cssPath,
                TailwindVersion.V4_1
            );

            Assert.NotNull(config.Blocklist);
            Assert.Contains("hover:bg-red-50", config.Blocklist!);
            Assert.Contains("focus:bg-red-100", config.Blocklist!);
            Assert.Contains("bg-red-300", config.Blocklist!);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
