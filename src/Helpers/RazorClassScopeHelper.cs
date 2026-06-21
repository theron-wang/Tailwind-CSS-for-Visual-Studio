using System.Linq;
using System.Text.RegularExpressions;

namespace TailwindCSSIntellisense.Helpers;

public static class RazorClassScopeHelper
{
    private const string Pattern = "class(es)?\\s*\\s*=\"";
    private const string PatternSingleQuote = "class(es)?\\s*\\s*='";

    // regexr.com/86qhv
    private const string ReversePattern = "\"\\s*=\\s*(se)?ssalc";

    /// <summary>
    /// Returns -1 if no class attribute is found; the first index of the class attribute
    /// if any matches the regex class\s*=\s*". Case is ignored.
    /// </summary>
    public static int GetFirstClassIndex(string text, bool useSingleQuote)
    {
        var match = Regex.Match(
            text,
            useSingleQuote ? PatternSingleQuote : Pattern,
            RegexOptions.IgnoreCase
        );

        if (match.Success)
        {
            return match.Index;
        }
        else
        {
            return -1;
        }
    }

    /// <summary>
    /// Returns -1 if no class attribute is found; the last index of the class attribute
    /// if any matches the regex class\s*=\s*". Case is ignored.
    /// </summary>
    public static int GetLastClassIndex(string text)
    {
        // Reverse the string and look backwards since there is no LastIndexOf method
        var match = Regex.Match(
            string.Join("", text.Reverse()),
            ReversePattern,
            RegexOptions.IgnoreCase
        );

        if (match.Success)
        {
            return text.Length - match.Index - match.Length;
        }
        else
        {
            return -1;
        }
    }
}
