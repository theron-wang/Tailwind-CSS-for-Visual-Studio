namespace TailwindCSSIntellisense.Helpers;

// Description tests exercise caching and variants, not the external color library.
internal static class ColorHelpers
{
    public static int ConversionCount { get; private set; }

    public static string? ConvertToHexIfPossible(string color)
    {
        ConversionCount++;
        return null;
    }

    public static bool IsHex(object value, out string? hex)
    {
        hex = null;
        return false;
    }
}
