using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace TailwindCSSIntellisense.Configuration;

/// <summary>
/// MEF Component class to provide methods to find files of a certain type in the open solution
/// </summary>
[Export]
public sealed class FileFinder
{
    private static readonly HashSet<string> SkipFolders = new(
        ["node_modules", "bin", "obj", ".vs", ".git"],
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Finds all Javascript files (.js) within the solution
    /// </summary>
    /// <returns>The absolute paths to all Javascript files</returns>
    internal Task<List<string>> GetJavascriptFilesAsync() =>
        FindAllFilesAsync(DefaultConfigurationFileNames.Extensions);

    /// <summary>
    /// Finds all CSS files (.css) within the solution
    /// </summary>
    /// <returns>The absolute paths to all CSS files</returns>
    internal Task<List<string>> GetCssFilesAsync() => FindAllFilesAsync(".css");

    /// <summary>
    /// Gets the path of the current miscellaneous project
    /// </summary>
    /// <returns>A <see cref="Task"/> which returns a <see cref="string"/> representing the folder path or <see langword="null"/> if the project does not exist</returns>
    internal async Task<string?> GetCurrentMiscellaneousProjectPathAsync()
    {
        // ToSolutionItemAsync must be done on the UI thread
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var vsSolution = await VS.Services.GetSolutionAsync();

        if (vsSolution is null)
        {
            return null;
        }

        var solution = await vsSolution.ToSolutionItemAsync();

        if (solution?.FullPath is null)
        {
            return null;
        }

        // If a solution is present, this project is not miscellaneous; just not loaded yet.
        // Return null to make the caller look elsewhere.
        if (solution.FullPath!.EndsWith(".sln") || solution.FullPath!.EndsWith(".slnx"))
        {
            return null;
        }

        return solution.FullPath;
    }

    private Task<List<string>> FindAllFilesAsync(params string[] extensions)
    {
        return TraverseAllProjectsAndFindFilesOfTypeAsync(extensions);
    }

    internal async Task<List<string>> TraverseAllProjectsAndFindFilesOfTypeAsync(
        IEnumerable<string> extensions
    )
    {
        var projects = await VS.Solutions.GetAllProjectsAsync();
        await TaskScheduler.Default;

        var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var paths = new List<string>();

        if (!projects.Any())
        {
            // If no projects, probably misc
            var miscPath = await GetCurrentMiscellaneousProjectPathAsync();
            await TaskScheduler.Default;

            if (string.IsNullOrEmpty(miscPath))
            {
                return [];
            }

            paths.Add(miscPath!);
        }
        else
        {
            paths.AddRange(projects.Select(p => Path.GetDirectoryName(p.FullPath)));
        }

        // Directory.Enumerate doesn't give the fine-grained control needed
        var files = await Task.Run(() =>
        {
            var result = new List<string>();
            var directories = new Stack<string>(paths);

            while (directories.Count > 0)
            {
                var directory = directories.Pop();

                try
                {
                    foreach (var subdirectory in Directory.EnumerateDirectories(directory))
                    {
                        var name = Path.GetFileName(subdirectory);

                        if (!SkipFolders.Contains(name))
                        {
                            directories.Push(subdirectory);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        if (extensionSet.Contains(Path.GetExtension(file)))
                        {
                            result.Add(file);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            return result;
        });

        return files;
    }
}
