using System.Globalization;
using System.Text.RegularExpressions;

namespace TailwindCSSIntellisense.Helpers;

public static class CssSizeConverter
{
    public static double CssSizeToPixels(string cssSize)
    {
        double baseFontSize = 16; // for rem and em
        double viewportWidth = 1920; // for vw
        double viewportHeight = 1080; // for vh

        // Return 0, not error, since we don't want this to be a major issue
        if (string.IsNullOrWhiteSpace(cssSize))
        {
            return 0;
        }

        var match = Regex.Match(cssSize.Trim(), @"^(?<value>[\d.]+)(?<unit>[a-zA-Z%]+)$");

        if (!match.Success)
        {
            return 0;
        }

        double value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        string unit = match.Groups["unit"].Value.ToLowerInvariant();

        return unit switch
        {
            "px" => value,
            "rem" => value * baseFontSize,
            "em" => value * baseFontSize,
            "vw" => value * viewportWidth / 100,
            "vh" => value * viewportHeight / 100,
            _ => 0,
        };
    }
}
