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

        var match = Regex.Match(
            cssSize.Trim(),
            @"^(?<value>[+-]?(?:\d+(?:\.\d*)?|\.\d+))(?<unit>[a-zA-Z%]+)$"
        );

        if (
            !match.Success
            || !double.TryParse(
                match.Groups["value"].Value,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value
            )
            || double.IsInfinity(value)
        )
        {
            return 0;
        }

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
