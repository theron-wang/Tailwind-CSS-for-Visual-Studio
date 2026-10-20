using System.Collections.Generic;

namespace TailwindCSSIntellisense.Configuration;

internal static class CssConfigSplitter
{
    internal static IEnumerable<string> Split(string css)
    {
        char? quote = null;
        var escaped = false;
        var depth = 0;
        var start = 0;
        // Scan once: quote-counting lookaheads rescan the remaining stylesheet
        // for every declaration and confuse apostrophes inside double quotes.
        for (var i = 0; i < css.Length; i++)
        {
            var current = css[i];
            if (escaped)
            {
                escaped = false;
            }
            else if (current == '\\')
            {
                escaped = true;
            }
            else if (quote is not null)
            {
                if (current == quote)
                {
                    quote = null;
                }
            }
            else if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current is '(' or '[')
            {
                depth++;
            }
            else if (current is ')' or ']')
            {
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (current == ';' && depth == 0)
            {
                var declaration = css.Substring(start, i - start).Trim();
                if (declaration.Length > 0)
                {
                    yield return declaration;
                }
                start = i + 1;
            }
        }
        var last = css.Substring(start).Trim();
        if (last.Length > 0)
        {
            yield return last;
        }
    }
}
