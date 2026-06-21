using System;
using System.Collections.Generic;
using System.Linq;

namespace TailwindCSSIntellisense.Helpers;

internal static class StringHelpers
{
    public static string ReplaceLastOccurrence(this string source, string find, string replace)
    {
        var place = source.LastIndexOf(find);

        if (place == -1)
        {
            return source;
        }

        return source.Remove(place, find.Length).Insert(place, replace);
    }

    public static string? GetClosestString(this string input, IEnumerable<string> candidates)
    {
        string? closest = null;
        int closestDistance = int.MaxValue;
        int closestCaseInsensitiveDistance = int.MaxValue;
        string inputLower = input.ToLowerInvariant();

        foreach (var candidate in candidates)
        {
            string candidateLower = candidate.ToLowerInvariant();
            int distance = GetLevenshteinDistance(inputLower, candidateLower);
            int caseInsensitiveDistance = GetLevenshteinDistance(input, candidate);

            if (
                distance < closestDistance
                || (
                    distance == closestDistance
                    && caseInsensitiveDistance < closestCaseInsensitiveDistance
                )
            )
            {
                closestDistance = distance;
                closestCaseInsensitiveDistance = caseInsensitiveDistance;
                closest = candidate;
            }
        }

        return closest;
    }

    public static string JoinWithCommasAndAnd(this IEnumerable<string> items)
    {
        var list = new List<string>(items);
        if (list.Count == 0)
        {
            return string.Empty;
        }

        if (list.Count == 1)
        {
            return list[0];
        }

        if (list.Count == 2)
        {
            return $"{list[0]} and {list[1]}";
        }

        return string.Join(", ", list.GetRange(0, list.Count - 1)) + $" and {list.Last()}";
    }

    private static int GetLevenshteinDistance(string a, string b)
    {
        int m = a.Length,
            n = b.Length;
        int[] prev = new int[n + 1];
        int[] curr = new int[n + 1];

        for (int j = 0; j <= n; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= m; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= n; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }
}
