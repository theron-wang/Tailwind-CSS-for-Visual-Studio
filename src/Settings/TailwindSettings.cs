using System.Collections.Immutable;
using TailwindCSSIntellisense.Options;

namespace TailwindCSSIntellisense.Settings;

/// <summary>
/// A complete settings snapshot. Create a replacement with a with expression;
/// published snapshots and their nested values are immutable.
/// </summary>
public sealed record TailwindSettings
{
    /// <summary>
    /// Note that this property is different from <see cref="TailwindSettingsProjectOnly.ConfigurationFiles"/>;
    /// this list contains CSS configuration files located in <see cref="BuildFiles"/>.
    /// </summary>
    public ImmutableList<ConfigurationFile> ConfigurationFiles { get; init; } = [];
    public string DefaultOutputCssName { get; init; } = "";
    public ImmutableList<string> OnSaveTriggerFileExtensions { get; init; } = [];
    public ImmutableList<BuildPair> BuildFiles { get; init; } = [];
    public string? PackageConfigurationFile { get; init; }
    public bool UseCli { get; init; }
    public string? TailwindCliPath { get; init; }

    /// <summary>
    /// Whether Tailwind is set to enable/disable in global settings
    /// </summary>
    public bool EnableTailwindCss { get; init; }
    public BuildProcessOptions BuildType { get; init; }
    public SortClassesOptions SortClassesType { get; init; }
    public SortClassesOptions CanonicalizeClassesType { get; init; } = SortClassesOptions.None;
    public string? BuildScript { get; init; }
    public bool OverrideBuild { get; init; }
    public bool AutomaticallyMinify { get; init; }
    public CustomRegexes CustomRegexes { get; init; } = new();
}
