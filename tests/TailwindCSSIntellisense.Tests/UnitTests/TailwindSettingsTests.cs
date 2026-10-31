using System.Text.Json;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class TailwindSettingsTests
{
    [Fact]
    public void UpdatingNestedSettings_PreservesPreviousSnapshot()
    {
        var original = new TailwindSettings
        {
            ConfigurationFiles = [new() { Path = "tailwind.config.js" }],
            BuildFiles = [new() { Input = "input.css", Output = "output.css" }],
            OnSaveTriggerFileExtensions = [".html"],
            CustomRegexes = new() { HTML = new() { Values = ["original"] } },
        };

        var updated = original with
        {
            BuildFiles = original.BuildFiles.SetItem(
                0,
                original.BuildFiles[0] with
                {
                    Output = "new.css",
                }
            ),
            ConfigurationFiles = original.ConfigurationFiles.Clear(),
            OnSaveTriggerFileExtensions = original.OnSaveTriggerFileExtensions.Add(".razor"),
            CustomRegexes = original.CustomRegexes with
            {
                HTML = original.CustomRegexes.HTML! with { Values = ["replacement"] },
            },
        };

        Assert.Equal("output.css", Assert.Single(original.BuildFiles).Output);
        Assert.Single(original.ConfigurationFiles);
        Assert.Equal([".html"], original.OnSaveTriggerFileExtensions);
        Assert.Equal("original", Assert.Single(original.CustomRegexes.HTML!.Values));
        Assert.Equal("new.css", Assert.Single(updated.BuildFiles).Output);
        Assert.Empty(updated.ConfigurationFiles);
        Assert.Equal([".html", ".razor"], updated.OnSaveTriggerFileExtensions);
        Assert.Equal("replacement", Assert.Single(updated.CustomRegexes.HTML!.Values));
    }

    [Fact]
    public void CollectionInterfaces_CannotMutatePublishedSettings()
    {
        var settings = new TailwindSettings
        {
            BuildFiles = [new() { Input = "input.css" }],
            ConfigurationFiles = [new() { Path = "tailwind.config.js" }],
            OnSaveTriggerFileExtensions = [".html"],
            CustomRegexes = new() { HTML = new() { Values = ["pattern"] } },
        };

        Assert.Throws<NotSupportedException>(() => ((IList<BuildPair>)settings.BuildFiles).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ConfigurationFile>)settings.ConfigurationFiles).Clear()
        );
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)settings.OnSaveTriggerFileExtensions).Clear()
        );
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)settings.CustomRegexes.HTML!.Values).Clear()
        );
    }

    [Fact]
    public void ExistingProjectJson_DeserializesImmutableNestedValues()
    {
        const string json = """
            {
              "BuildFiles": [{ "Input": "input.css", "Output": "output.css", "Behavior": "Minified" }],
              "ConfigurationFiles": [{ "Path": "tailwind.config.js" }],
              "CustomRegexes": { "HTML": { "Override": true, "Values": ["pattern"] }, "Razor": null }
            }
            """;

        var project = JsonSerializer.Deserialize<TailwindSettingsProjectOnly>(json)!;
        var restored = JsonSerializer.Deserialize<TailwindSettingsProjectOnly>(
            JsonSerializer.Serialize(project)
        )!;

        Assert.Equal("input.css", Assert.Single(restored.BuildFiles!).Input);
        Assert.Equal(BuildBehavior.Minified, restored.BuildFiles![0].Behavior);
        Assert.Equal("tailwind.config.js", Assert.Single(restored.ConfigurationFiles!).Path);
        Assert.True(restored.CustomRegexes.HTML!.Override);
        Assert.Equal("pattern", Assert.Single(restored.CustomRegexes.HTML.Values));
        Assert.Null(restored.CustomRegexes.Razor);
    }
}
