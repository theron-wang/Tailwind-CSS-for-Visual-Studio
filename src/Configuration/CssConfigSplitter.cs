using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TailwindCSSIntellisense.Configuration;

internal static class CssConfigSplitter
{
    private static readonly Regex _cssSemicolonSplitter = new(
        @"(?<!\\);(?=(?:(?:[^""']*[""']){2})*[^""']*$)",
        RegexOptions.Compiled
    );

    internal static IEnumerable<string> Split(string css)
    {
        return _cssSemicolonSplitter
            .Split(css)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s));
    }
}
