using System.Collections.Immutable;

namespace TailwindCSSIntellisense.Settings;

public sealed record CustomRegexes
{
    public CustomRegex? Razor { get; init; } = new();
    public CustomRegex? HTML { get; init; } = new();
    public CustomRegex? JavaScript { get; init; } = new();

    public sealed record CustomRegex
    {
        public bool Override { get; init; }
        public ImmutableList<string> Values { get; init; } = [];
    }
}
