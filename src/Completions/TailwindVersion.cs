namespace TailwindCSSIntellisense.Completions;

public enum TailwindVersion
{
    V3,
    V4,
    V4_1,
    V4_2,
    V4_3,

    LATEST = V4_3
}

public static class TailwindVersionExtensions
{
    public static TailwindVersion GetMajorVersion(this TailwindVersion version)
    {
        return version switch
        {
            TailwindVersion.V4_1 or TailwindVersion.V4_2 or TailwindVersion.V4_3 => TailwindVersion.V4,
            _ => version
        };
    }
}