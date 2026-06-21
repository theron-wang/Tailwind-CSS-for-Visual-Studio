using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace TailwindCSSIntellisense.Helpers;

internal static class ITextBufferExtensions
{
    /// <summary>
    /// Equivalent to <see cref="ITextBuffer.GetFileName"/> but ensures it is called on the UI thread, as required by Visual Studio APIs. Should be used in any context where the file name of a text buffer is needed, as calling <see cref="ITextBuffer.GetFileName"/> from a non-UI thread can lead to exceptions or undefined behavior.
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
