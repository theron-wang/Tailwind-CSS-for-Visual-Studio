using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace TailwindCSSIntellisense.Helpers;

internal static class ITextBufferExtensions
{
    /// <summary>
    /// Calls <c>buffer.GetFileName()</c> on the UI thread, as required by Visual Studio APIs.
    /// Use this wrapper when retrieving a text buffer's file name from a background thread.
    /// </summary>
    /// <param name="buffer">The buffer</param>
    /// <returns>The file name</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "Not expensive"
    )]
    public static string GetFileNameSafe(this ITextBuffer buffer)
    {
        return ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return buffer.GetFileName()!;
        });
    }
}
