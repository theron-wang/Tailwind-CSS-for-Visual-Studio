namespace TailwindCSSIntellisense.Helpers;

internal static class CommentRemover
{
    /// <summary>
    /// Removes CSS comments (/* ... */) from the input string while preserving string literals and newlines.
    /// The length and indices of actual text are preserved.
    /// </summary>
    /// <param name="css">The CSS string from which to remove comments</param>
    /// <returns>The CSS string with comments removed</returns>
    public static string StripCssComments(string css)
    {
        var result = new System.Text.StringBuilder(css);
        int i = 0;

        while (i < css.Length)
        {
            // Skip string literals
            if (css[i] == '"' || css[i] == '\'')
            {
                char quote = css[i];
                i++;
                while (i < css.Length && css[i] != quote)
                {
                    if (css[i] == '\\')
                    {
                        i++;
                    }

                    i++;
                }
                i++; // closing quote
                continue;
            }

            // Block comment: /* ... */
            if (i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i + 1 < css.Length && !(css[i] == '*' && css[i + 1] == '/'))
                {
                    i++;
                }

                i += 2; // closing */
                for (int j = start; j < i && j < css.Length; j++)
                {
                    if (result[j] != '\n' && result[j] != '\r')
                    {
                        result[j] = ' ';
                    }
                }
                continue;
            }

            i++;
        }

        return result.ToString();
    }
}
