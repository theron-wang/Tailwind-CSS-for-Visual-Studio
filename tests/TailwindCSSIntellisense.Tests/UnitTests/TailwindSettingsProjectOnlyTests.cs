using System.Text.Json;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class TailwindSettingsProjectOnlyTests
{
    [Theory]
    [InlineData(BuildBehavior.Default)]
    [InlineData(BuildBehavior.Minified)]
    [InlineData(BuildBehavior.Unminified)]
    public void FromSettings_PreservesPathsAndPreferencesWhenRelocating(BuildBehavior behavior)
    {
        var oldDirectory = Path.Combine(Path.GetTempPath(), "project");
        var destination = Path.Combine(oldDirectory, "styles");
        var input = Path.Combine(destination, "input#1%20.css");
        var output = Path.Combine(oldDirectory, "dist", "output.css");
        var config = Path.Combine(destination, "tailwind.config.js");
        var package = Path.Combine(oldDirectory, "package.json");
        var settings = new TailwindSettings
        {
            ConfigurationFiles = [new() { Path = input }, new() { Path = config }],
            BuildFiles =
            [
                new()
                {
                    Input = input,
                    Output = output,
                    Behavior = behavior,
                },
            ],
            PackageConfigurationFile = package,
            UseCli = true,
            CustomRegexes = new()
            {
                HTML = new() { Override = true, Values = ["custom-pattern"] },
            },
        };

        var saved = TailwindSettingsProjectOnly.FromSettings(settings, destination);
        var restored = JsonSerializer.Deserialize<TailwindSettingsProjectOnly>(
            JsonSerializer.Serialize(saved)
        )!;

        var pair = Assert.Single(restored.BuildFiles!);
        Assert.Equal(input, PathHelpers.GetAbsolutePath(destination, pair.Input));
        Assert.Equal(output, PathHelpers.GetAbsolutePath(destination, pair.Output));
        Assert.Equal(behavior, pair.Behavior);
        Assert.Equal(
            config,
            PathHelpers.GetAbsolutePath(
                destination,
                Assert.Single(restored.ConfigurationFiles!).Path
            )
        );
        Assert.Equal(
            package,
            PathHelpers.GetAbsolutePath(destination, restored.PackageConfigurationFile)
        );
        Assert.True(restored.UseCli);
        Assert.True(restored.CustomRegexes.HTML!.Override);
        Assert.Equal("custom-pattern", Assert.Single(restored.CustomRegexes.HTML.Values));
        Assert.Equal(input, settings.BuildFiles[0].Input);
        Assert.Equal(output, settings.BuildFiles[0].Output);
    }

    [Fact]
    public void FromSettings_OmitsCssConfigurationAlreadyUsedAsBuildInput()
    {
        var directory = Path.Combine(Path.GetTempPath(), "project");
        var input = Path.Combine(directory, "input.css");
        var settings = new TailwindSettings
        {
            ConfigurationFiles = [new() { Path = input.ToUpperInvariant() }],
            BuildFiles = [new() { Input = input }],
        };

        var saved = TailwindSettingsProjectOnly.FromSettings(settings, directory);

        Assert.Null(saved.ConfigurationFiles);
        Assert.Equal("", Assert.Single(saved.BuildFiles!).Output);
    }
}
