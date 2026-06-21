using System.Collections.Generic;

namespace TailwindCSSIntellisense.Helpers;

internal static class ConfigurationThemeTokenizer
{
    /// <summary>
    /// Splits a theme configuration key into its segments, handling both dot notation and bracket notation.
    /// </summary>
    /// <param name="input">The theme configuration key to tokenize.</param>
    /// <returns>A list of the tokenized segments.</returns>
    public static List<string> TokenizeTheme(string input)
    {
        List<string> segments = [];
        int startIndex = 0;

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '[')
            {
                int endIndex = input.IndexOf(']', i);

                if (endIndex != -1)
                {
                    string segment = input.Substring(startIndex, i - startIndex).Trim();
                    if (!string.IsNullOrEmpty(segment))
                    {
                        segments.Add(segment);
                    }

                    segment = input.Substring(i, endIndex - i + 1).Trim('[', ']');
                    segments.Add(segment);

                    startIndex = endIndex + 1;
                    i = endIndex;
                }
            }
            else if (input[i] == '.')
            {
                string segment = input.Substring(startIndex, i - startIndex).Trim();
                if (!string.IsNullOrEmpty(segment))
                {
                    segments.Add(segment);
                }

                startIndex = i + 1;
            }
        }

        string lastSegment = input.Substring(startIndex).Trim();
        if (!string.IsNullOrEmpty(lastSegment))
        {
            segments.Add(lastSegment);
        }

        return segments;
    }
}
