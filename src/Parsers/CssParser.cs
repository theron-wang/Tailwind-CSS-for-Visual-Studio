using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;

namespace TailwindCSSIntellisense.Parsers;

internal static class CssParser
{
    private static readonly Regex _segmentRegex = new(
        @"[@;{}](?:""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|[^;{}])*",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );

    public static IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span)
    {
        if (span.Start == span.Snapshot.Length)
        {
            yield break;
        }

        // Expand backward to the nearest boundary so we don't start mid-segment
        int start = span.Start;
        while (start > 0 && !IsBoundary(span.Snapshot[start]))
        {
            start--;
        }

        // Expand forward to the nearest boundary so we don't end mid-segment
        int end = span.End;
        while (end < span.Snapshot.Length)
        {
            if (IsBoundary(span.Snapshot[end]))
            {
                end++;
                break;
            }
            end++;
        }

        var text = CommentRemover.StripCssComments(span.Snapshot.GetText(start, end - start));

        foreach (Match match in _segmentRegex.Matches(text))
        {
            if (string.IsNullOrWhiteSpace(match.Value))
            {
                continue;
            }

            int segStart = start + match.Index;
            int segEnd = segStart + match.Length;

            if (segEnd > span.Snapshot.Length)
            {
                segEnd = span.Snapshot.Length;
            }

            yield return new SnapshotSpan(span.Snapshot, segStart, segEnd - segStart);
        }
    }

    private static bool IsBoundary(char c) => c is '@' or ';' or '{' or '}';
}
