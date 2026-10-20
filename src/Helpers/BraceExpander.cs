using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

public static class BraceExpander
{
    public static List<string> Expand(string input)
    {
        var results = new HashSet<string>();
        ExpandRecursive(input, results);
        return new List<string>(results);
    }

    private static void ExpandRecursive(string input, HashSet<string> results)
    {
        var match = Regex.Match(input, @"\{([^{}]*)\}");
        if (!match.Success)
        {
            results.Add(input);
            return;
        }

        var prefix = input.Substring(0, match.Index);
        var suffix = input.Substring(match.Index + match.Length);
        var options = match.Groups[1].Value;

        foreach (var option in ExpandOptions(options))
        {
            ExpandRecursive(prefix + option + suffix, results);
        }
    }

    private static IEnumerable<string> ExpandOptions(string options)
    {
        // Handle range with step (e.g., 1..10..2)
        var rangeMatch = Regex.Match(options, @"^(\d+)\.\.(\d+)(?:\.\.(\d+))?$");
        if (rangeMatch.Success)
        {
            var step = 1;
            if (
                !int.TryParse(rangeMatch.Groups[1].Value, out var start)
                || !int.TryParse(rangeMatch.Groups[2].Value, out var end)
                || (
                    rangeMatch.Groups[3].Success
                    && !int.TryParse(rangeMatch.Groups[3].Value, out step)
                )
                || step == 0
            )
            {
                throw new ArgumentException(
                    "Brace ranges require valid integers and a positive step.",
                    nameof(options)
                );
            }

            // A wider counter prevents wrapping at int.MaxValue and looping forever.
            long increment = start <= end ? step : -step;
            for (long i = start; increment > 0 ? i <= end : i >= end; i += increment)
            {
                yield return i.ToString(CultureInfo.InvariantCulture);
            }
            yield break;
        }

        // Handle comma-separated values (e.g., a,b,c)
        foreach (var option in options.Split(','))
        {
            yield return option.Trim();
        }
    }
}
