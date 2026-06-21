using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TailwindCSSIntellisense.Initialization;

internal abstract class ClassTypeBase
{
    [JsonPropertyName("s")]
    public string Stem { get; set; } = null!;

    [JsonPropertyName("dv")]
    public List<string>? DirectVariants { get; set; }

    [JsonPropertyName("c")]
    public bool? UseColors { get; set; }

    [JsonPropertyName("sp")]
    public bool? UseSpacing { get; set; }

    [JsonPropertyName("n")]
    public bool? HasNegative { get; set; }
}
