using System.Collections.Generic;
using TailwindCSSIntellisense.Options;

namespace TailwindCSSIntellisense.Settings;

public class TailwindSettings
{
    /// <summary>
    /// Note that this property is different from <see cref="TailwindSettingsProjectOnly.ConfigurationFiles"/>;
    /// this list contains CSS configuration files located in <see cref="BuildFiles"/>.
    /// </summary>
    public List<ConfigurationFile> ConfigurationFiles { get; set; } = [];
    public string DefaultOutputCssName { get; set; } = "";
    public string[] OnSaveTriggerFileExtensions { get; set; } = [];
    public List<BuildPair> BuildFiles { get; set; } = [];
    public string? PackageConfigurationFile { get; set; }
    public bool UseCli { get; set; }
    public string? TailwindCliPath { get; set; }

    /// <summary>
    /// Whether Tailwind is set to enable/disable in global settings
    /// </summary>
    public bool EnableTailwindCss { get; set; }
    public BuildProcessOptions BuildType { get; set; }
    public SortClassesOptions SortClassesType { get; set; }
    public string? BuildScript { get; set; }
    public bool OverrideBuild { get; set; }
    public bool AutomaticallyMinify { get; set; }
    public CustomRegexes CustomRegexes { get; set; } = new();
}
