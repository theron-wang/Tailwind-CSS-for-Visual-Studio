using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;

namespace TailwindCSSIntellisense.Tests.UnitTests;

[Collection("External processes")]
public class ConfigFileParserTests
{
    [Theory]
    [InlineData("tailwindcss", "tw-")]
    [InlineData("tailwindcss/index", "tw-")]
    [InlineData("tailwindcss/utilities", null)]
    [InlineData("node_modules/tailwindcss/index.css", "tw-")]
    [InlineData("./node_modules/tailwindcss/index.css", "tw-")]
    public async Task GetConfigurationAsync_ParsesCssSourcesPrefixUtilitiesAndVariants(
        string tailwindImport,
        string? expectedPrefix
    )
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
                $$"""
                @import "{{tailwindImport}}" source("./src") prefix(tw-);
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

            Assert.Equal(expectedPrefix, config.Prefix);
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
                @source not inline("{hover:,focus:,}bg-red-{50,{100..300..100}}");
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

    [Theory]
    [InlineData("@source inline(\"bg-red-500\");")]
    [InlineData("@source not inline(\"bg-red-500\");", TailwindVersion.V4)]
    public async Task GetConfigurationAsync_DoesNotBlocklistIncludedOrUnsupportedInlineSources(
        string source,
        TailwindVersion version = TailwindVersion.V4_1
    )
    {
        await WithCssFilesAsync(
            new() { ["app.css"] = source },
            config => Assert.Empty(config.Blocklist ?? []),
            version
        );
    }

    [Fact]
    public async Task GetConfigurationAsync_KeepsBlocklistsFromAllImports()
    {
        await WithCssFilesAsync(
            new()
            {
                ["app.css"] = "@import './a.css'; @import './b.css';",
                ["a.css"] = "@source not inline(\"hidden\");",
                ["b.css"] = "@source not inline(\"flex\");",
            },
            config => Assert.Equal(new[] { "flex", "hidden" }, config.Blocklist!.Order())
        );
    }

    [Fact]
    public async Task GetConfigurationAsync_ParsesPrefixWhenAutomaticSourcesAreDisabled()
    {
        await WithCssFilesAsync(
            new() { ["app.css"] = "@import 'tailwindcss' prefix(tw) source(none);" },
            config =>
            {
                Assert.Equal("tw", config.Prefix);
                Assert.Empty(config.ContentPaths);
            }
        );
    }

    [Fact]
    public async Task GetConfigurationAsync_ResolvesPluginImportPaths()
    {
        await WithCssFilesAsync(
            new()
            {
                ["app.css"] = "@import './my-tailwindcss-components.css';",
                ["my-tailwindcss-components.css"] = "@utility example { display: block; }",
            },
            config => Assert.Contains("example", config.PluginClasses)
        );
    }

    [Fact]
    public async Task GetConfigurationAsync_HandlesCyclicCssImports()
    {
        await WithCssFilesAsync(
            new()
            {
                ["app.css"] = "@import './a.css'; @utility root { display: block; }",
                ["a.css"] = "@import './app.css'; @utility child { display: flex; }",
            },
            config => Assert.Equal(new[] { "child", "root" }, config.PluginClasses.Order())
        );
    }

    private static async Task WithCssFilesAsync(
        Dictionary<string, string> files,
        Action<TailwindConfiguration> verify,
        TailwindVersion version = TailwindVersion.V4_1
    )
    {
        var root = Path.Combine(Path.GetTempPath(), $"tailwind-config-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            foreach (var file in files)
            {
                await File.WriteAllTextAsync(Path.Combine(root, file.Key), file.Value);
            }
            verify(
                await ConfigFileParser.GetConfigurationAsync(Path.Combine(root, "app.css"), version)
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"{;/*literal*/}\"")]
    [InlineData("\"font's name\"")]
    [InlineData("\"escaped\\\"{;value}\"")]
    public async Task GetConfigurationAsync_PreservesQuotedThemeValuesAndFollowingDeclarations(
        string value
    )
    {
        await WithCssFilesAsync(
            new() { ["app.css"] = $"@theme {{ --custom: {value}; --following: 123; }}" },
            config =>
            {
                Assert.Equal(value, config.ThemeVariables["--custom"]);
                Assert.Equal("123", config.ThemeVariables["--following"]);
            }
        );
    }

    [Theory]
    [InlineData("prefix(tw) source('./src')")]
    [InlineData("source('./src') prefix(tw)")]
    public async Task GetConfigurationAsync_ParsesPrefixAndSourceInEitherOrder(string options)
    {
        await WithCssFilesAsync(
            new() { ["app.css"] = $"@import 'tailwindcss' {options};" },
            config =>
            {
                Assert.Equal("tw", config.Prefix);
                Assert.EndsWith("src", Assert.Single(config.ContentPaths));
            }
        );
    }

    [Fact]
    public async Task GetConfigurationAsync_ExpandsEachExcludedInlineCandidateIndependently()
    {
        await WithCssFilesAsync(
            new() { ["app.css"] = "@source not inline('hidden {hover:,}flex');" },
            config =>
                Assert.Equal(new[] { "flex", "hidden", "hover:flex" }, config.Blocklist!.Order())
        );
    }
}
