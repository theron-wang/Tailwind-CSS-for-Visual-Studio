namespace TailwindCSSIntellisense.Settings;

public sealed record ConfigurationFile
{
    public string Path { get; init; } = null!;
}
