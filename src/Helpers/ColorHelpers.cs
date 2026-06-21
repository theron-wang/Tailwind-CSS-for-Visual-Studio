using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Wacton.Unicolour;

namespace TailwindCSSIntellisense.Helpers;

internal static class ColorHelpers
{
    public static bool IsHex(object value, out string? hex)
    {
        if (value.ToString().LastIndexOf('#') != 0)
        {
            hex = null;
            return false;
        }

        var content = value.ToString().Trim('#').ToUpper();
        var hexLetters = "ABCDEF";
        if (content.All(c => char.IsNumber(c) || hexLetters.Contains(c)))
        {
            if (content.Length == 6 || content.Length == 8)
            {
                hex = content.Substring(0, 6);
                return true;
            }
            else if (content.Length == 3)
            {
                hex = content;
                return true;
            }
        }

        hex = null;
        return false;
    }

    /// <summary>
    /// Converts rgb, oklch to hex
    /// Input be in the format rgb(# # #) or oklch(# # #)
    /// </summary>
    /// <param name="color">The color string to convert</param>
    /// <returns>The converted hex color string, or null if conversion is not possible (i.e., out of gamut)</returns>
    public static string? ConvertToHexIfPossible(string color)
    {
        if (color.StartsWith("#"))
        {
            return color;
        }

        if (color.StartsWith("rgb"))
        {
            var rgb = color.Replace("rgb(", "").Replace(")", "").Trim();

            byte[] values;
            if (rgb.Contains(','))
            {
                values =
                [
                    .. rgb.Split(',')
                        .Take(3)
                        .Where(v => byte.TryParse(v, out _))
                        .Select(byte.Parse),
                ];
            }
            else
            {
                values =
                [
                    .. rgb.Split(' ')
                        .Take(3)
                        .Where(v => byte.TryParse(v, out _))
                        .Select(byte.Parse),
                ];
            }

            if (values.Length != 3)
            {
                return null;
            }
            return $"#{values[0]:X2}{values[1]:X2}{values[2]:X2}";
        }

        if (color.StartsWith("oklch"))
        {
            var oklch = color.Replace("oklch(", "").Replace(")", "").Trim();

            var values = oklch
                .Split(' ')
                .Select(x =>
                    x.EndsWith("%")
                        ? double.TryParse(
                            x.Substring(0, x.Length - 1),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var value
                        )
                            ? (value / 100).ToString(CultureInfo.InvariantCulture)
                            : x
                        : x
                )
                .Where(x =>
                    double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                )
                .Select(num => double.Parse(num, CultureInfo.InvariantCulture))
                .ToList();

            if (values.Count != 3)
            {
                return null;
            }

            var unicolour = new Unicolour(ColourSpace.Oklch, values[0], values[1], values[2]);

            var rgb = unicolour.Rgb;
            return rgb.Byte255.Hex == "-" ? null : rgb.Byte255.Hex;
        }

        if (color.StartsWith("hsl"))
        {
            var hsl = color.Replace("hsl(", "").Replace(")", "").Trim();

            var values = hsl.Split(' ')
                .Select(x =>
                    x.EndsWith("%")
                        ? double.TryParse(
                            x.Substring(0, x.Length - 1),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var value
                        )
                            ? (value / 100).ToString(CultureInfo.InvariantCulture)
                            : x
                        : x
                )
                .Where(x =>
                    double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                )
                .Select(num => double.Parse(num, CultureInfo.InvariantCulture))
                .ToList();

            if (values.Count != 3)
            {
                return null;
            }

            var unicolour = new Unicolour(ColourSpace.Hsl, (values[0], values[1], values[2]));

            return unicolour.Rgb.Byte255.Hex == "-" ? null : unicolour.Rgb.Byte255.Hex;
        }

        return null;
    }

    /// <summary>
    /// Converts hex, oklch to rgb
    /// Input be in the format #hex or oklch(# # #)
    /// For colors outside the gamut, this will return the closest possible color within the gamut, which may not be the intended color
    /// </summary>
    public static int[]? ForceConvertToRgb(string color)
    {
        if (color.StartsWith("#"))
        {
            var hex = color.TrimStart('#');
            if (hex.Length == 3)
            {
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            }

            // Unicolour does not accept 3-letter hex codes
            var unicolour = new Unicolour($"#{hex}");
            return
            [
                unicolour.Rgb.Clipped.Byte255.R,
                unicolour.Rgb.Clipped.Byte255.G,
                unicolour.Rgb.Clipped.Byte255.B,
            ];
        }

        if (color.StartsWith("oklch"))
        {
            var oklch = color.Replace("oklch(", "").Replace(")", "").Trim();

            var values = oklch
                .Split(' ')
                .Select(x =>
                    x.EndsWith("%")
                        ? double.TryParse(
                            x.Substring(0, x.Length - 1),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var value
                        )
                            ? (value / 100).ToString(CultureInfo.InvariantCulture)
                            : x
                        : x
                )
                .Where(x =>
                    double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                )
                .Select(num => double.Parse(num, CultureInfo.InvariantCulture))
                .ToList();

            if (values.Count != 3)
            {
                return null;
            }

            var unicolour = new Unicolour(ColourSpace.Oklch, values[0], values[1], values[2]);

            return
            [
                unicolour.Rgb.Clipped.Byte255.R,
                unicolour.Rgb.Clipped.Byte255.G,
                unicolour.Rgb.Clipped.Byte255.B,
            ];
        }

        if (color.StartsWith("hsl"))
        {
            var hsl = color.Replace("hsl(", "").Replace(")", "").Trim();

            var values = hsl.Split(' ')
                .Select(x =>
                    x.EndsWith("%")
                        ? double.TryParse(
                            x.Substring(0, x.Length - 1),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var value
                        )
                            ? (value / 100).ToString(CultureInfo.InvariantCulture)
                            : x
                        : x
                )
                .Where(x =>
                    double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                )
                .Select(num => double.Parse(num, CultureInfo.InvariantCulture))
                .ToList();

            if (values.Count != 3)
            {
                return null;
            }

            var unicolour = new Unicolour(ColourSpace.Hsl, values[0], values[1], values[2]);

            return
            [
                unicolour.Rgb.Clipped.Byte255.R,
                unicolour.Rgb.Clipped.Byte255.G,
                unicolour.Rgb.Clipped.Byte255.B,
            ];
        }

        if (color.StartsWith("rgb"))
        {
            var rgb = color.Replace("rgb(", "").Replace(")", "").Trim();

            List<byte> values;
            if (rgb.Contains(','))
            {
                values =
                [
                    .. rgb.Split(',')
                        .Where(x => byte.TryParse(x, out _))
                        .Select(x => byte.Parse(x)),
                ];
            }
            else
            {
                values =
                [
                    .. rgb.Split(' ')
                        .Where(x => byte.TryParse(x, out _))
                        .Select(x => byte.Parse(x)),
                ];
            }

            if (values.Count >= 3)
            {
                return [values[0], values[1], values[2]];
            }
        }

        return null;
    }
}
