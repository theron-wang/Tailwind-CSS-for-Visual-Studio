using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Threading;
using TailwindCSSIntellisense.Options;

namespace TailwindCSSIntellisense.Node;

/// <summary>
/// Helper class which provides methods to update and check for Tailwind npm module updates
/// </summary>
internal static class CheckForUpdates
{
    private static readonly HashSet<string> _checkedDirectories = [];

    /// <summary>
    /// Updates the Tailwind CSS module in a configuration file's folder, if needed
    /// </summary>
    /// <param name="folder">The file to update</param>
    public static async Task UpdateConfigFileFolderAsync(string config)
    {
        if (string.IsNullOrWhiteSpace(config))
        {
            return;
        }

        var folder = Path.GetDirectoryName(config)?.ToLower();

        var general = await General.GetLiveInstanceAsync();

        if (
            !general.UseTailwindCss
            || !general.AutomaticallyUpdateLibrary
            || !File.Exists(config)
            || string.IsNullOrWhiteSpace(folder)
        )
        {
            return;
        }

        if (folder!.EndsWith(Path.DirectorySeparatorChar.ToString()) == false)
        {
            folder += Path.DirectorySeparatorChar;
        }

        // Prevents multiple projects in same solution from re-checking the same file
        if (_checkedDirectories.Contains(folder))
        {
            return;
        }

        try
        {
            // There is no concern in running outdated on v3 for @tailwindcss/cli, since
            // a missing package simply returns {}
            await UpdateModuleAsync(folder, "@tailwindcss/cli");
            await UpdateModuleAsync(folder, "tailwindcss");
            _checkedDirectories.Add(folder);
        }
        catch (Exception ex)
        {
            await VS.StatusBar.ShowMessageAsync(
                "Tailwind CSS update/check failed; check 'Extensions' output window for more details"
            );
            await ex.LogAsync();
        }
    }

    private static async Task UpdateModuleAsync(string folder, string module)
    {
        var workingDir = Path.GetDirectoryName(folder);

        if (string.IsNullOrEmpty(workingDir))
        {
            return;
        }

        var processInfo = new ProcessStartInfo()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            FileName = "cmd",
            Arguments = $"/C npm outdated --json",
            WorkingDirectory = workingDir,
            StandardOutputEncoding = Encoding.UTF8,
        };

        string output;

        using (var process = Process.Start(processInfo))
        {
            /*
             * Sample output:

             {
              "@tailwindcss/cli": {
                "current": "4.1.12",
                "wanted": "4.2.1",
                "latest": "4.2.1",
                "dependent": "project_name",
                "location": "path/to/project_name"
              },
              "tailwindcss": {
                "current": "4.1.12",
                "wanted": "4.2.1",
                "latest": "4.2.1",
                "dependent": "project_name",
                "location": "path/to/project_name"
              }

            */
            output = await process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();
        }

        var result = JsonSerializer.Deserialize<JsonObject>(output);

        if (result is null || result.ContainsKey(module) == false)
        {
            await VS.StatusBar.ShowMessageAsync($"Tailwind CSS: {module} is up to date");
            return;
        }

        OutdatedPackage? relevantPackage = null;

        if (result[module] is JsonObject jsonObj)
        {
            relevantPackage = jsonObj.Deserialize<OutdatedPackage>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }

        if (
            relevantPackage is null
            || relevantPackage.Current is null
            || relevantPackage.Latest is null
            || relevantPackage.Wanted is null
        )
        {
            await VS.StatusBar.ShowMessageAsync($"Tailwind CSS: {module} is up to date");
            return;
        }

        // Avoid updating major versions: 3.x --> 4.x, for example
        var currentMajor = relevantPackage.Current.Split('.')[0];
        var newMajor = relevantPackage.Latest.Split('.')[0];

        // If the current version is v3.x, then Wanted will be the latest v3.x version, and Latest will be the latest v4.x version.
        // In this case, we want to notify the user about the new major version, but not automatically update, since that could cause breaking changes.
        if (currentMajor != newMajor)
        {
            await VS.StatusBar.ShowMessageAsync(
                $"A major Tailwind update is available: {relevantPackage.Latest}. If you would like to update, please manually run npm install {module}@latest."
            );
        }

        if (relevantPackage.Current == relevantPackage.Wanted)
        {
            return;
        }

        await VS.StatusBar.ShowMessageAsync(
            $"Updating {module} ({relevantPackage.Current} -> {relevantPackage.Wanted})"
        );

        processInfo = new ProcessStartInfo()
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
            FileName = "cmd",
            Arguments = $"/C npm install {module}@{relevantPackage.Wanted}",
            WorkingDirectory = Path.GetDirectoryName(folder),
            StandardErrorEncoding = Encoding.UTF8,
        };

        string error;

        using (var process = Process.Start(processInfo))
        {
            error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            var ex = new Exception(error);
            await ex.LogAsync();
            await VS.StatusBar.ShowMessageAsync(
                "An error occurred while updating Tailwind CSS: check the 'Extensions' output window for more details"
            );
            return;
        }

        await VS.StatusBar.ShowMessageAsync(
            $"Tailwind CSS update successful (updated to version {relevantPackage.Wanted})"
        );
    }

    private class OutdatedPackage
    {
        public string? Current { get; set; }
        public string? Wanted { get; set; }
        public string? Latest { get; set; }
        public string? Dependent { get; set; }
        public string? Location { get; set; }
    }
}
