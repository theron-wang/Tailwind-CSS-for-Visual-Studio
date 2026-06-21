using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TailwindCSSIntellisense.Settings;

internal class TailwindSettingsProjectOnly
{
    private const string Schema =
        "https://raw.githubusercontent.com/theron-wang/Tailwind-CSS-for-Visual-Studio/refs/heads/main/tailwind.extension.schema.json";

    [JsonPropertyName("$schema")]
    public string SchemaProperty => Schema;

    [Obsolete]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    /// <summary>
    /// Maintained for backwards compatibility: use <see cref="ConfigurationFiles"/> instead.
    /// </summary>
    public string? ConfigurationFile { get; set; }

    [Obsolete]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    /// <summary>
    /// Maintained for backwards compatibility: use <see cref="BuildFiles"/> instead.
    /// </summary>
    public string? InputCssFile { get; set; }

    [Obsolete]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    /// <summary>
    /// Maintained for backwards compatibility: use <see cref="BuildFiles"/> instead.
    /// </summary>
    public string? OutputCssFile { get; set; }

    /// <summary>
    /// For v4 projects, CSS configuration files that are already tracked via <see cref="BuildFiles"/>
    /// should not be persisted in this list to avoid duplication.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ConfigurationFile>? ConfigurationFiles { get; set; } = [];
    public List<BuildPair> BuildFiles { get; set; } = [];
    public string? PackageConfigurationFile { get; set; }
    public CustomRegexes CustomRegexes { get; set; } = new();
    public bool UseCli { get; set; }
}
