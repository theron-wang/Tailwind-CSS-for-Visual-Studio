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
}
