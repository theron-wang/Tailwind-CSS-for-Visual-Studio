using System.Collections.Generic;
using System.Collections.Immutable;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Helpers;

internal static class KnownModifiers
{
    public static readonly IReadOnlyDictionary<string, string> GradientModifierToDescription =
        new Dictionary<string, string>()
        {
            { "oklab", "oklab" },
            { "oklch", "oklch" },
            { "srgb", "srgb" },
            { "hsl", "hsl" },
            { "longer", "oklch longer hue" },
            { "shorter", "oklch shorter hue" },
            { "increasing", "oklch increasing hue" },
            { "decreasing", "oklch decreasing hue" },
        }.ToImmutableDictionary();

    public static bool IsEligibleForLineHeightModifier(
        string className,
        ProjectCompletionValues project
    )
    {
        return className.StartsWith("text-")
            && !className.StartsWith("text-shadow")
            && project.CssVariables.ContainsKey($"--{className.Split('/')[0]}");
    }
}
