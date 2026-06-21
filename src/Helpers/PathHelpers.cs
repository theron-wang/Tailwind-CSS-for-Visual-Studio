using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TailwindCSSIntellisense.Helpers;

public static class PathHelpers
{
    // https://stackoverflow.com/questions/703281/getting-path-relative-to-the-current-working-directory
    /// <summary>
    /// Gets the relative path of a file with respect to a specified folder.
    /// </summary>
    /// <param name="file">The full path of the file.</param>
    /// <param name="folder">The full path of the folder.</param>
    /// <returns>
    /// A string representing the relative path of the file with respect to the folder.
    /// If the file parameter is null/empty, the method returns null.
    /// </returns>
    public static string? GetRelativePath(string? file, string folder)
    {
        if (string.IsNullOrEmpty(file))
        {
            return null;
        }

        var pathUri = new Uri(file);
        // Folders must end in a slash
        if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            folder += Path.DirectorySeparatorChar;
        }
        var folderUri = new Uri(folder);
        return Uri.UnescapeDataString(
            folderUri.MakeRelativeUri(pathUri).ToString().Replace('/', Path.DirectorySeparatorChar)
        );
    }

    /// <summary>
    /// Gets the absolute path by combining a base directory with a relative path.
    /// </summary>
    /// <param name="dir">The base directory path.</param>
    /// <param name="rel">The relative path to be combined with the base directory.</param>
    /// <returns>
    /// A string representing the absolute path obtained by combining the base directory
    /// and the relative path. If the relative path parameter is null/empty, the method returns null.
    /// </returns>
    public static string? GetAbsolutePath(string dir, string? rel)
    {
        if (string.IsNullOrEmpty(rel))
        {
            return null;
        }
        // Folders must end in a slash
        if (!dir.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            dir += Path.DirectorySeparatorChar;
        }

        var dirUri = new Uri(dir);
        var absUri = new Uri(dirUri, rel);

        return Uri.UnescapeDataString(
            absUri.AbsolutePath.Replace('/', Path.DirectorySeparatorChar)
        );
    }

    /// <summary>
    /// Checks if a given path matches a glob pattern.
    /// Supports *, **, and {} for globbing.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>True if the path matches the pattern, otherwise false.</returns>
    public static bool PathMatchesGlob(string path, string pattern)
    {
        if (path == null || pattern == null)
        {
            return false;
        }

        // Normalize path separators for consistency
        path = path.ToLower().Replace('\\', '/');
        pattern = pattern.ToLower().Replace('\\', '/');

        string regexPattern = "^" + GlobToRegex(pattern) + "$";
        return Regex.IsMatch(path, regexPattern);
    }

    private static string GlobToRegex(string pattern)
    {
        var regex = new System.Text.StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    // ** => match any characters including slashes
                    regex.Append(".*");
                    i += 2;
                }
                else
                {
                    // * => match any characters except slash
                    regex.Append("[^/]*");
                    i++;
                }
            }
            else if (c == '?')
            {
                regex.Append("."); // match any single character
                i++;
            }
            else if (c == '{')
            {
                // handle {a,b,c}
                int end = pattern.IndexOf('}', i);
                if (end == -1)
                {
                    regex.Append("\\{");
                    i++;
                }
                else
                {
                    string group = pattern.Substring(i + 1, end - i - 1);
                    string[] options = group.Split(',');
                    regex.Append("(?:");
                    regex.Append(string.Join("|", options));
                    regex.Append(")");
                    i = end + 1;
                }
            }
            else
            {
                // Escape regex special characters
                if ("\\.[]{}()+-^$|".IndexOf(c) >= 0)
                {
                    regex.Append('\\');
                }

                regex.Append(c);
                i++;
            }
        }

        return regex.ToString();
    }
}
