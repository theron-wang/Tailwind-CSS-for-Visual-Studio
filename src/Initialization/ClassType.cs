using System.Collections.Generic;
using System.Text.Json.Serialization;
using TailwindCSSIntellisense.Completions.V4;

namespace TailwindCSSIntellisense.Initialization;

/// <summary>
/// v4+ ONLY
/// </summary>
internal class ClassType : ClassTypeBase
{
    [JsonPropertyName("sv")]
    public List<ClassSubType>? Subvariants { get; set; }

    [JsonPropertyName("p")]
    public bool? UsePercent { get; set; }

    [JsonPropertyName("f")]
    public bool? UseFractions { get; set; }

    [JsonPropertyName("d")]
    public bool? UseNumbers { get; set; }

    /// <summary>
    /// The absence of this property does not necessarily mean that arbitrary values are not supported.
    /// Colors, spacing, percentages, fractions, numbers all support this already, but theirs will be
    /// set to null. This property is only for the ones that don't fall into those categories.
    /// </summary>
    [JsonPropertyName("a")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasArbitrary { get; set; }
}
