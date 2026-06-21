using System.Linq;

namespace TailwindCSSIntellisense.Helpers;

public static class DefaultConfigurationFileNames
{
    /// <summary>
    /// [".js", ".cjs", ".mjs", ".ts", ".cts", ".mts"]
    /// </summary>
    public static readonly string[] Extensions = [".js", ".cjs", ".mjs", ".ts", ".cts", ".mts"];

    /// <summary>
    /// ["tailwind.config.js", "tailwind.config.cjs", "tailwind.config.mjs", "tailwind.config.ts", "tailwind.config.cts", "tailwind.config.mts"]
    /// </summary>
    public static readonly string[] Names = Extensions.Select(e => $"tailwind.config{e}").ToArray();
}
