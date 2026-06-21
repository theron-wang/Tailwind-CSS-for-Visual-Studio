using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TailwindCSSIntellisense.Configuration;

/// <summary>
/// MEF Component Class which provides a method to search for the Tailwind configuration file
/// </summary>
[Export]
public sealed class ConfigFileScanner
{
    private string? _configFilePath;

    [Import]
    internal FileFinder FileFinder { get; set; } = null!;

    /// <summary>
    /// Searches through the solution to find a Tailwind CSS configuration file.
    /// </summary>
    /// <returns>A <see cref="Task"/> of type <see cref="string" />, which represents the absolute path to an existing configuration file, or null if one cannot be found</returns>
    internal async Task<string?> TryFindConfigurationFileAsync()
    {
        var cssFiles = await FileFinder.GetCssFilesAsync();

        // Priority: check for @import "tailwindcss";
        foreach (var file in cssFiles)
        {
            if (await DoesFileContainAsync(file, "@import", "tailwindcss"))
            {
                _configFilePath = file;
                return _configFilePath;
            }
        }

        var jsFiles = await FileFinder.GetJavascriptFilesAsync();

        // Best case scenario: user names file tailwind.config.js
        _configFilePath = jsFiles.FirstOrDefault(f =>
            DefaultConfigurationFileNames.Names.Contains(Path.GetFileName(f).ToLower())
        );

        if (_configFilePath != null)
        {
            return _configFilePath;
        }

        // Next: search all css files and scrape for @config

        string? cssTargetFile = null;

        foreach (var file in cssFiles)
        {
            if (await DoesFileContainAsync(file, "@config"))
            {
                cssTargetFile = file;
                break;
            }
        }

        if (cssTargetFile != null)
        {
            _configFilePath = await ExtractConfigJsPathAsync(cssTargetFile);
        }

        if (_configFilePath != null && File.Exists(_configFilePath))
        {
            return _configFilePath;
        }

        return null;
    }

    private async Task<bool> DoesFileContainAsync(string filePath, string text)
    {
        using var fs = File.OpenRead(filePath);
        using var reader = new StreamReader(fs);

        // Read up to line 15
        for (int i = 0; i < 15; i++)
        {
            var line = await reader.ReadLineAsync();

            if (line is null)
            {
                break;
            }

            if (line.Contains(text))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a file contains text and text1 on the same line. For example, use @import and tailwindcss to match @import "tailwindcss";
    /// </summary>
    private async Task<bool> DoesFileContainAsync(string filePath, string text, string text1)
    {
        using var fs = File.OpenRead(filePath);
        using var reader = new StreamReader(fs);

        // Read up to line 15
        for (int i = 0; i < 15; i++)
        {
            var line = await reader.ReadLineAsync();

            if (line is null)
            {
                break;
            }

            if (line.Contains(text) && line.Contains(text1))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string?> ExtractConfigJsPathAsync(string filePath)
    {
        string? configLine = null;
        // Read up to line 15
        var lines = 0;
        using (var fs = File.OpenRead(filePath))
        {
            using var reader = new StreamReader(fs);
            var line = await reader.ReadLineAsync();
            lines++;

            if (line.Contains("@config"))
            {
                configLine = line.Trim();
                goto End;
            }

            if (lines > 15)
            {
                goto End;
            }
        }

        End:

        if (configLine == null)
        {
            return null;
        }
        var indexOfConfig = configLine.IndexOf("@config");
        var indexOfSemicolon = configLine.IndexOf(';', indexOfConfig);

        string scanText;
        if (indexOfSemicolon == -1)
        {
            scanText = configLine.Substring(indexOfConfig);
        }
        else
        {
            scanText = configLine.Substring(indexOfConfig, indexOfSemicolon - indexOfConfig);
        }

        try
        {
            var relPath = scanText.Split(' ')[1].Trim('\'').Trim('\"');

            // @config provides a relative path to configuration file
            // To find the path of the config file, we must take the relative path in terms
            // of the absolute path of the css file
            return Uri.UnescapeDataString(new Uri(new Uri(filePath), relPath).AbsolutePath);
        }
        catch
        {
            // @config syntax is invalid, cannot parse path
            return null;
        }
    }
}
