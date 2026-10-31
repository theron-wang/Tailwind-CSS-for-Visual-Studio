using System.Text.Json.Serialization;

namespace TailwindCSSIntellisense.Settings;

public sealed record BuildPair
{
    public string Input { get; init; } = "";
    public string Output { get; init; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BuildBehavior Behavior { get; init; } = BuildBehavior.Default;
}
