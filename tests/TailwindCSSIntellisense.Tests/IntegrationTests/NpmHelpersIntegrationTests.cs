using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Node;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

[Collection("External processes")]
public class NpmHelpersIntegrationTests
{
    [Fact]
    public async Task GetConfigurationAsync_LocalPluginImportsUseFilePathsForReloading()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tailwind-plugin-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var cssPath = Path.Combine(root, "app.css");
            var pluginPath = Path.Combine(root, "plugin.js");
            await File.WriteAllTextAsync(cssPath, "@plugin './plugin.js';");
            await File.WriteAllTextAsync(
                pluginPath,
                "module.exports = { handler: ({ addUtilities }) => addUtilities({ '.audit-utility': { display: 'block' } }), config: {} };"
            );

            var config = await ConfigFileParser
                .GetConfigurationAsync(cssPath, TailwindVersion.V4_1)
                .WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(pluginPath, Assert.Single(config.Imports));
            Assert.Contains("audit-utility", config.PluginClasses);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("audit-local-css-plugin", "{\"style\":\"dist/styles.css\"}", "dist/styles.css")]
    [InlineData("@audit-local/css-plugin", "{\"style\":\"style.css\"}", "style.css")]
    [InlineData("audit-local-css-plugin", "{}", null)]
    [InlineData("audit-local-css-plugin", "{\"style\":false}", null)]
    [InlineData("audit-local-css-plugin", null, null)]
    public async Task GetCssPluginMainFileAsync_UsesInstalledPackageMetadataWithoutRegistryAccess(
        string package,
        string? manifest,
        string? expectedStyle
    )
    {
        var root = Path.Combine(Path.GetTempPath(), $"tailwind-npm-tests-{Guid.NewGuid():N}");
        var packageDir = Path.Combine(root, "node_modules", package);
        Directory.CreateDirectory(packageDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "package.json"), "{\"private\":true}");
            // The lookup must work offline, even for a package absent from npm.
            await File.WriteAllTextAsync(
                Path.Combine(root, ".npmrc"),
                "registry=http://127.0.0.1:1\nfetch-retries=0"
            );
            if (manifest is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(packageDir, "package.json"), manifest);
            }

            var result = await NpmHelpers
                .GetCssPluginMainFileAsync(root, package)
                .WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(
                expectedStyle is null
                    ? ""
                    : Path.GetFullPath(Path.Combine(packageDir, expectedStyle)),
                result
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
