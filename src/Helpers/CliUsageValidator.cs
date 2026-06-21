using System.IO;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Helpers;

internal static class CliUsageValidator
{
    public static bool IsCliUsedCorrectly(TailwindSettings settings)
    {
        return !settings.UseCli
            || (
                !string.IsNullOrWhiteSpace(settings.TailwindCliPath)
                && File.Exists(settings.TailwindCliPath)
            );
    }
}
