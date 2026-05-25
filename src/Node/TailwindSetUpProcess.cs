using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace TailwindCSSIntellisense.Node;

/// <summary>
/// Helper class to provide methods to install and set up Tailwind
/// </summary>
[Export]
internal sealed class TailwindSetUpProcess
{
    public bool IsSettingUp { get; private set; }

    [Import]
    public DirectoryVersionFinder DirectoryVersionFinder { get; set; } = null!;

    /// <summary>
    /// Starts a process to install Tailwind in the specified directory (uses npm), if needInstall = true. Otherwise, it just creates a tailwind.css file with the import statement.
    /// </summary>
    /// <param name="directory">The directory to install in</param>
    public async Task<string?> RunAsync(string directory, bool needInstall, bool shouldCreateTailwindDotCss)
    {
        IsSettingUp = true;
        var processInfo = new ProcessStartInfo()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            FileName = "cmd",
            WorkingDirectory = directory,
            Arguments = "/C npm install -D tailwindcss @tailwindcss/cli",
        };

        try
        {
            await VS.StatusBar.ShowMessageAsync("Setting up Tailwind CSS");

            if (needInstall)
            {
                await VS.StatusBar.StartAnimationAsync(StatusAnimation.General);
                using var process = Process.Start(processInfo);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.ErrorDataReceived += ErrorDataReceived;

                await process.WaitForExitAsync();

                DirectoryVersionFinder.ClearCacheForDirectory(directory);
            }

            if (shouldCreateTailwindDotCss)
            {
                string fileName;

                if (File.Exists(Path.Combine(directory, "tailwind.css")))
                {
                    fileName = $"tailwind-{Guid.NewGuid().ToString().Substring(0, 8)}.css";
                }
                else
                {
                    fileName = "tailwind.css";
                }

                using (var fileStream = new FileStream(Path.Combine(directory, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
                {
                    using var streamWriter = new StreamWriter(fileStream);
                    await streamWriter.WriteLineAsync("@import \"tailwindcss\";");
                }

                return Path.Combine(directory, fileName);
            }

            return null;
        }
        catch (Exception ex)
        {
            await LogErrorAsync(ex);
        }
        finally
        {
            IsSettingUp = false;
            await VS.StatusBar.EndAnimationAsync(StatusAnimation.General);
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync", Justification = "FileAndForget ok")]
    private void ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            if (e.Data != null)
            {
                var ex = new Exception(e.Data);
                await LogErrorAsync(ex);
            }
        }).FileAndForget(nameof(TailwindCSSIntellisense) + "/TailwindSetUpProcess/ErrorDataReceived");
    }

    private async Task LogErrorAsync(Exception exception)
    {
        await exception.LogAsync();
        await VS.StatusBar.ShowMessageAsync("An error occurred while setting up Tailwind CSS: check the 'Extensions' output window for more details");
    }
}
