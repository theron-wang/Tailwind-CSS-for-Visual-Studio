using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;

namespace TailwindCSSIntellisense.Node;

internal static class NpmHelpers
{
    /// <summary>
    /// Asynchronously retrieves the global npm root directory.
    /// </summary>
    /// <remarks>The npm root directory is where globally installed npm packages are stored.
    /// This method requires that npm is installed and available in the system's PATH.</remarks>
    /// <returns>A task that represents the asynchronous operation. The task result contains the full path to the global npm root
    /// directory.</returns>
    public static async Task<string> GetGlobalNpmRootAsync()
    {
        var processStartInfo = GetCmdProcessStartInfo("npm root -g");

        using Process process =
            Process.Start(processStartInfo)
            ?? throw new InvalidOperationException("Failed to start npm process.");

        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();

        await Task.WhenAll(errorTask, outputTask, process.WaitForExitAsync());
        var error = await errorTask;
        var output = await outputTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"npm root -g failed (exit {process.ExitCode}): {error.Trim()}"
            );
        }

        return output.Trim();
    }

    /// <summary>
    /// Asynchronously retrieves the local npm root directory for the specified working directory.
    /// </summary>
    /// <remarks>The npm root directory is where locally installed npm packages are stored for the specified
    /// working directory. This method requires that npm is installed and available in the system's PATH.</remarks>
    /// <param name="workingDir">The path to the working directory in which to execute the npm command. Cannot be null or empty.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the full path to the local npm root
    /// directory.</returns>
    public static async Task<string> GetLocalNpmRootAsync(string workingDir)
    {
        var processStartInfo = GetCmdProcessStartInfo("npm root");
        processStartInfo.WorkingDirectory = workingDir;

        using Process process =
            Process.Start(processStartInfo)
            ?? throw new InvalidOperationException("Failed to start npm process.");

        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();

        await Task.WhenAll(errorTask, outputTask, process.WaitForExitAsync());
        var error = await errorTask;
        var output = await outputTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"npm root failed (exit {process.ExitCode}): {error.Trim()}"
            );
        }

        return output.Trim();
    }

    /// <summary>
    /// For a Tailwind CSS CSS plugin (using @import instead of @plugin), gets the main file of the plugin
    /// specified in the `style` attribute in the package's package.json.
    /// </summary>
    /// <param name="workingDir">The working directory</param>
    /// <param name="package">The name of the Tailwind CSS CSS plugin package.</param>
    /// <returns>The absolute path of the CSS plugin main file or an empty string if none was found</returns>
    public static async Task<string> GetCssPluginMainFileAsync(string workingDir, string package)
    {
        var processStartInfo = GetCmdProcessStartInfo($"npm view {package} style");
        processStartInfo.WorkingDirectory = workingDir;

        string relativePath;
        using (
            Process process =
                Process.Start(processStartInfo)
                ?? throw new InvalidOperationException("Failed to start npm process.")
        )
        {
            var errorTask = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();

            await Task.WhenAll(errorTask, outputTask, process.WaitForExitAsync());
            var error = await errorTask;
            relativePath = await outputTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"npm view {package} style failed (exit {process.ExitCode}): {error.Trim()}"
                );
            }
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "";
        }

        var packageDir = await GetLocalNpmRootAsync(workingDir);

        return PathHelpers.GetAbsolutePath(packageDir, package + "/" + relativePath.Trim())!;
    }

    /// <summary>
    /// Gets a ProcessStartInfo representing cmd. No working directory specified.
    /// </summary>
    /// <param name="command">The command; i.e., npm root</param>
    /// <returns>The process start info</returns>
    private static ProcessStartInfo GetCmdProcessStartInfo(string command)
    {
        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/C {command}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
    }
}
