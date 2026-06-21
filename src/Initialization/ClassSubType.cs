using System.Text.Json.Serialization;
using TailwindCSSIntellisense.Initialization;

namespace TailwindCSSIntellisense.Completions.V4;

internal class ClassSubType : ClassSubTypeBase
{
    /// <summary>
    /// The absence of this property does not necessarily mean that arbitrary values are not supported.
    /// Colors, spacing, percentages, fractions, numbers all support this already, but theirs will be
    /// set to null. This property is only for the ones that don't fall into those categories.
    /// </summary>
    [JsonPropertyName("a")]
    public bool? HasArbitrary { get; set; }
}
