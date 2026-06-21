using System.Text.Json.Serialization;

namespace TailwindCSSIntellisense.Settings;

public class BuildPair
{
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BuildBehavior Behavior { get; set; } = BuildBehavior.Default;
}
