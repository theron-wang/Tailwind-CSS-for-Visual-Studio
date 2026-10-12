using System.Text;
using System.Text.RegularExpressions;

namespace TailwindCSSIntellisense.Helpers;

internal static class ProcessArgumentHelpers
{
    // Quote for a directly launched Windows executable, not for cmd or PowerShell.
    public static string QuoteWindowsArgument(string value)
    {
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
            }
            else
            {
                result.Append('\\', backslashes);
            }
            result.Append(character);
            backslashes = 0;
        }

        result.Append('\\', backslashes * 2);
        return result.Append('"').ToString();
    }

    public static bool IsNpmPackageVersion(string value)
    {
        return value.Length <= 256
            && Regex.IsMatch(
                value,
                @"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z"
            );
    }
}
