using TailwindCSSIntellisense.Configuration;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class ConfigFileScannerTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    [InlineData(0, false)]
    public async Task ConfigScanner_FindsConfigWithinTheFirstFifteenLines(int line, bool expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cssPath = Path.Combine(directory, "input.css");
            var configPath = Path.Combine(directory, "custom.config.js");
            await File.WriteAllTextAsync(configPath, "module.exports = {};");
            await File.WriteAllLinesAsync(
                cssPath,
                line == 0
                    ? []
                    : Enumerable
                        .Repeat("/* comment */", line - 1)
                        .Append("@config './custom.config.js';")
            );
            var finder = new FileFinder();
            finder.CssFiles.Add(cssPath);
            var scanner = new ConfigFileScanner { FileFinder = finder };

            var result = await scanner.TryFindConfigurationFileAsync();

            Assert.Equal(
                expected ? configPath : null,
                result is null ? null : Path.GetFullPath(result)
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
