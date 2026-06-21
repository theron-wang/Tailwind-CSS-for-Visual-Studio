using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Configuration;

internal static class ThemeVariableHelpers
{
    public static string? GetCssVariableNamespace(string variable, TailwindVersion version)
    {
        if (variable.StartsWith("--color-"))
        {
            return "--color-";
        }
        else if (variable.StartsWith("--font-weight-"))
        {
            return "--font-weight-";
        }
        else if (variable.StartsWith("--font-"))
        {
            return "--font-";
        }
        else if (variable.StartsWith("--text-shadow-") && version >= TailwindVersion.V4_1)
        {
            return "--text-shadow-";
        }
        else if (variable.StartsWith("--text-"))
        {
            return "--text-";
        }
        else if (variable.StartsWith("--tracking-"))
        {
            return "--tracking-";
        }
        else if (variable.StartsWith("--leading-"))
        {
            return "--leading-";
        }
        else if (variable.StartsWith("--breakpoint-"))
        {
            return "--breakpoint-";
        }
        else if (variable.StartsWith("--container-"))
        {
            // v4
            return "--container-";
        }
        else if (variable.StartsWith("--spacing-"))
        {
            return "--spacing-";
        }
        else if (variable.StartsWith("--radius-"))
        {
            return "--radius-";
        }
        else if (variable.StartsWith("--shadow-"))
        {
            return "--shadow-";
        }
        else if (variable.StartsWith("--inset-shadow-"))
        {
            // v4
            return "--inset-shadow-";
        }
        else if (variable.StartsWith("--drop-shadow-"))
        {
            return "--drop-shadow-";
        }
        else if (variable.StartsWith("--blur-"))
        {
            return "--blur-";
        }
        else if (variable.StartsWith("--perspective-"))
        {
            // v4
            return "--perspective-";
        }
        else if (variable.StartsWith("--aspect-"))
        {
            return "--aspect-";
        }
        else if (variable.StartsWith("--ease-"))
        {
            return "--ease-";
        }
        else if (variable.StartsWith("--animate-"))
        {
            return "--animate-";
        }

        return null;
    }

    public static string? GetConfigurationClassStemFromCssVariable(
        string variable,
        TailwindVersion version
    )
    {
        if (variable.StartsWith("--color-"))
        {
            return "colors";
        }
        else if (variable.StartsWith("--font-weight-"))
        {
            return "fontWeight";
        }
        else if (variable.StartsWith("--font-"))
        {
            return "fontFamily";
        }
        else if (variable.StartsWith("--text-shadow") && version >= TailwindVersion.V4_1)
        {
            // 4.1+
            return "v4_1-text-shadow";
        }
        else if (variable.StartsWith("--text-"))
        {
            return "fontSize";
        }
        else if (variable.StartsWith("--tracking-"))
        {
            return "letterSpacing";
        }
        else if (variable.StartsWith("--leading-"))
        {
            return "lineHeight";
        }
        else if (variable.StartsWith("--breakpoint-"))
        {
            return "screens";
        }
        else if (variable.StartsWith("--container-"))
        {
            // v4
            return "v4-container";
        }
        else if (variable.StartsWith("--spacing-"))
        {
            return "spacing";
        }
        else if (variable.StartsWith("--radius-"))
        {
            return "borderRadius";
        }
        else if (variable.StartsWith("--shadow-"))
        {
            return "boxShadow";
        }
        else if (variable.StartsWith("--inset-shadow-"))
        {
            // v4
            return "v4-insetShadow";
        }
        else if (variable.StartsWith("--drop-shadow-"))
        {
            return "dropShadow";
        }
        else if (variable.StartsWith("--blur-"))
        {
            return "blur";
        }
        else if (variable.StartsWith("--perspective-"))
        {
            // v4
            return "v4-perspective";
        }
        else if (variable.StartsWith("--aspect-"))
        {
            return "aspectRatio";
        }
        else if (variable.StartsWith("--ease-"))
        {
            return "transitionTimingFunction";
        }
        else if (variable.StartsWith("--animate-"))
        {
            return "animation";
        }

        return null;
    }

    public static string? FormatConfigurationPathFromCssVariable(
        string variable,
        TailwindVersion version
    )
    {
        string Stem(string prefix, string key) =>
            key + "." + variable.Substring(prefix.Length).Replace('-', '.');

        if (variable.StartsWith("--color-"))
        {
            return Stem("--color-", "colors");
        }
        else if (variable.StartsWith("--font-weight-"))
        {
            return Stem("--font-weight-", "fontWeight");
        }
        else if (variable.StartsWith("--font-"))
        {
            return Stem("--font-", "fontFamily");
        }
        else if (variable.StartsWith("--text-shadow-") && version >= TailwindVersion.V4_1)
        {
            return Stem("--text-shadow-", "textShadow");
        }
        else if (variable.StartsWith("--text-"))
        {
            return Stem("--text-", "fontSize");
        }
        else if (variable.StartsWith("--tracking-"))
        {
            return Stem("--tracking-", "letterSpacing");
        }
        else if (variable.StartsWith("--leading-"))
        {
            return Stem("--leading-", "lineHeight");
        }
        else if (variable.StartsWith("--breakpoint-"))
        {
            return Stem("--breakpoint-", "screens");
        }
        else if (variable.StartsWith("--container-"))
        {
            return Stem("--container-", "container");
        }
        else if (variable.StartsWith("--spacing-"))
        {
            return Stem("--spacing-", "spacing");
        }
        else if (variable.StartsWith("--radius-"))
        {
            return Stem("--radius-", "borderRadius");
        }
        else if (variable.StartsWith("--shadow-"))
        {
            return Stem("--shadow-", "boxShadow");
        }
        else if (variable.StartsWith("--inset-shadow-"))
        {
            return Stem("--inset-shadow-", "insetShadow");
        }
        else if (variable.StartsWith("--drop-shadow-"))
        {
            return Stem("--drop-shadow-", "dropShadow");
        }
        else if (variable.StartsWith("--blur-"))
        {
            return Stem("--blur-", "blur");
        }
        else if (variable.StartsWith("--perspective-"))
        {
            return Stem("--perspective-", "perspective");
        }
        else if (variable.StartsWith("--aspect-"))
        {
            return Stem("--aspect-", "aspectRatio");
        }
        else if (variable.StartsWith("--ease-"))
        {
            return Stem("--ease-", "transitionTimingFunction");
        }
        else if (variable.StartsWith("--animate-"))
        {
            return Stem("--animate-", "animation");
        }

        return null;
    }

    public static string? GetCssVariableFromConfigurationClassStem(string key)
    {
        if (key == "colors")
        {
            return "--color";
        }
        else if (key == "fontWeight")
        {
            return "--font-weight";
        }
        else if (key == "fontFamily")
        {
            return "--font";
        }
        else if (key == "textShadow")
        {
            return "--text-shadow";
        }
        else if (key == "fontSize")
        {
            return "--text";
        }
        else if (key == "letterSpacing")
        {
            return "--tracking";
        }
        else if (key == "lineHeight")
        {
            return "--leading";
        }
        else if (key == "screens")
        {
            return "--breakpoint";
        }
        else if (key == "spacing")
        {
            return "--spacing";
        }
        else if (key == "borderRadius")
        {
            return "--radius";
        }
        else if (key == "boxShadow")
        {
            return "--shadow";
        }
        else if (key == "insetShadow")
        {
            return "--inset-shadow";
        }
        else if (key == "dropShadow")
        {
            return "--drop-shadow";
        }
        else if (key == "blur")
        {
            return "--blur";
        }
        else if (key == "perspective")
        {
            return "--perspective";
        }
        else if (key == "aspectRatio")
        {
            return "--aspect";
        }
        else if (key == "transitionTimingFunction")
        {
            return "--ease";
        }
        else if (key == "animation")
        {
            return "--animate";
        }

        return null;
    }
}
