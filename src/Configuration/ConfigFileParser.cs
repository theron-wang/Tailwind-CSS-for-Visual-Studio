using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Node;

namespace TailwindCSSIntellisense.Configuration;

/// <summary>
/// Parses the Tailwind CSS configuration file
/// </summary>
internal static class ConfigFileParser
{
    /// <summary>
    /// Asynchronously parses a configuration file using a Node.js script and returns its contents as a JSON object.
    /// </summary>
    /// <remarks>This method relies on an external Node.js script to perform the parsing. Ensure that Node.js
    /// is installed and accessible in the system environment. The method reads both standard output and error streams
    /// from the process to capture results and errors.</remarks>
    /// <param name="path">The full file system path to the configuration file to be parsed. Cannot be null or empty.</param>
    /// <param name="isAPlugin">A value indicating whether the configuration file represents a plugin. This may affect how the file is
    /// processed.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a JsonObject representing the parsed
    /// configuration file, or null if the file is empty or cannot be deserialized.</returns>
    /// <exception cref="InvalidOperationException">Thrown if an error occurs while parsing the configuration file.</exception>
    private static async Task<JsonObject?> GetConfigJsonNodeAsync(string path, bool isAPlugin)
    {
        var scriptLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Resources", "parser.js");

        var processInfo = new ProcessStartInfo()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            FileName = "cmd",
            Arguments = $"/c node \"{scriptLocation}\" \"{Path.GetFileName(path)}\" {(isAPlugin ? "--plugin" : "")}",
            WorkingDirectory = Path.GetDirectoryName(path)
        };

        var localNodePath = await GetNodeModulesFromConfigFilePathAsync(path);
        await SetupNodeProcessAsync(processInfo, localNodePath);

        using var process = new Process();
        process.StartInfo = processInfo;

        var file = new StringBuilder();

        process.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
        {
            if (string.IsNullOrEmpty(e.Data) == false)
            {
                file.AppendLine(e.Data);
            }
        };
        var hasError = false;
        var error = new StringBuilder();
        process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
        {
            if (string.IsNullOrEmpty(e.Data) == false && e.Data.Contains("warn") == false)
            {
                hasError = true;
                error.AppendLine(e.Data);
            }
        };

        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (hasError)
        {
            throw new InvalidOperationException("Error occurred while parsing configuration file: " + error.ToString().Trim());
        }

        var fileText = file.ToString().Trim();

        return JsonSerializer.Deserialize<JsonObject>(fileText);
    }

    /// <summary>
    /// Gets the configuration settings from the Tailwind CSS configuration file.
    /// </summary>
    /// <returns>Returns a <see cref="Task{TailwindConfiguration}" /> of type <see cref="TailwindConfiguration"/> which contains the parsed configuration information</returns>
    internal static Task<TailwindConfiguration> GetConfigurationAsync(string path, TailwindVersion version)
    {
        if (Path.GetExtension(path) == ".css")
        {
            return GetCssConfigurationAsync(path, version);
        }
        else
        {
            return GetJavaScriptConfigurationAsync(path);
        }
    }

    private static async Task<TailwindConfiguration> GetCssConfigurationAsync(string path, TailwindVersion version)
    {
        var fullText = "";

        using (var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using var reader = new StreamReader(fileStream);

            fullText = await reader.ReadToEndAsync();
        }

        // Remove any text inside {}, since all theme values are in the base theme block
        // We need this because there may be multiple blocks on one line
        var themeTrimmed = new StringBuilder();

        var imports = new HashSet<string>();
        var utilities = new Dictionary<string, string>();
        var variants = new Dictionary<string, string>();
        var content = new List<string>();
        var blocklist = new List<string>();

        var level = 0;
        var inComment = false;

        var directive = "";
        var buildingDirective = false;

        var directiveParameter = "";
        var buildingDirectiveParameter = false;

        var prefix = "";

        bool isSourceSpecified = false;

        for (int i = 0; i < fullText.Length; i++)
        {
            var current = fullText[i];

            if (current == '/' && (!buildingDirectiveParameter || !(directiveParameter.Count(p => p == '\'') == 1 || directiveParameter.Count(p => p == '"') == 1)))
            {
                if (i + 1 < fullText.Length && fullText[i + 1] == '*')
                {
                    inComment = true;
                }
                else if (i > 0 && fullText[i - 1] == '*')
                {
                    inComment = false;
                    continue;
                }
            }

            if (inComment)
            {
                continue;
            }

            if (current == '{' && !buildingDirectiveParameter)
            {
                level++;
            }
            else if (current == '}' && !buildingDirectiveParameter)
            {
                level--;

                if (level == 0)
                {
                    directive = "";
                    directiveParameter = "";
                }
            }

            if (current == '@' && level == 0 && !buildingDirectiveParameter)
            {
                directive = "";
                buildingDirective = true;
                continue;
            }

            if (buildingDirective)
            {
                if (char.IsLetter(current) || current == '-')
                {
                    directive += current;
                }
                else
                {
                    buildingDirective = false;
                    buildingDirectiveParameter = true;
                    directiveParameter = "";
                }
                continue;
            }

            if (buildingDirectiveParameter)
            {
                if (current == ';')
                {
                    directiveParameter = directiveParameter.Trim();
                    buildingDirectiveParameter = false;

                    if (directive == "import")
                    {
                        var firstQuote = directiveParameter.IndexOfAny(['\'', '"']);
                        var secondQuote = directiveParameter.IndexOfAny(['\'', '"'], firstQuote + 1);

                        if (firstQuote == -1 || secondQuote == -1)
                        {
                            continue;
                        }

                        // Handle: @import './_components.css' layer(components);
                        var import = directiveParameter.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Trim();

                        // Handle these cases:
                        // @import url("https://fonts.googleapis.com/css2?family=Roboto&display=swap");
                        // @import "some-plugin";
                        if (import != "tailwindcss" && !directiveParameter.StartsWith("url"))
                        {
                            var importPath = PathHelpers.GetAbsolutePath(Path.GetDirectoryName(path)!, import.EndsWith(".css") ? import : import + ".css");

                            if (File.Exists(importPath))
                            {
                                imports.Add($"@import{importPath}");
                            }
                            else
                            {
                                // Look in node_modules
                                var importPathFromPackage = await NpmHelpers.GetCssPluginMainFileAsync(Path.GetDirectoryName(path)!, import);
                                // Use importPath in case the package CSS file cannot be found, so an exception will be thrown later down the line
                                imports.Add($"@import{(string.IsNullOrEmpty(importPathFromPackage) ? importPath : importPathFromPackage)}");
                            }
                        }
                        // Handle @import "tailwindcss" plus optional source(...) and prefix(...)
                        else if (import == "tailwindcss")
                        {
                            if (directiveParameter.IndexOf("source(", secondQuote) is int index && index != -1)
                            {
                                isSourceSpecified = true;
                                if (directiveParameter.Substring(index + 7).Trim().StartsWith("none"))
                                {
                                    continue;
                                }

                                firstQuote = directiveParameter.IndexOfAny(['\'', '"'], index);
                                secondQuote = directiveParameter.IndexOfAny(['\'', '"'], firstQuote + 1);

                                if (firstQuote == -1 || secondQuote == -1)
                                {
                                    continue;
                                }

                                var source = directiveParameter.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Trim();

                                source = PathHelpers.GetAbsolutePath(Path.GetDirectoryName(path)!, source)!;
                                content.Add(source);
                            }

                            if (directiveParameter.IndexOf("prefix(", secondQuote) is int prefixIndex && prefixIndex != -1)
                            {
                                var endParen = directiveParameter.IndexOf(')', prefixIndex);

                                if (endParen == -1)
                                {
                                    continue;
                                }

                                prefix = directiveParameter.Substring(prefixIndex + 7, endParen - prefixIndex - 7).Trim();
                            }
                        }
                        continue;
                    }
                    else if (directive == "config")
                    {
                        var import = directiveParameter.Replace("\"", "").Replace("'", "").TrimEnd(';').Trim();

                        import = PathHelpers.GetAbsolutePath(Path.GetDirectoryName(path)!, import);
                        imports.Add($"@config{import}");
                        continue;
                    }
                    else if (directive == "plugin")
                    {
                        var plugin = directiveParameter.Replace("\"", "").Replace("'", "").TrimEnd(';').Trim();

                        var pluginAbsPath = PathHelpers.GetAbsolutePath(Path.GetDirectoryName(path)!, plugin);

                        // If this path exists, then it is not a package name. If not, then we must resolve the package
                        if (!File.Exists(pluginAbsPath))
                        {
                            var resolvedPath = await ResolveFilePathFromNPMPackageNameAsync(path, plugin);
                            if (resolvedPath is not null)
                            {
                                imports.Add($"@plugin{resolvedPath}");
                            }
                        }
                        else
                        {
                            imports.Add($"@plugin{pluginAbsPath}");
                        }

                        continue;
                    }
                    // Handle short-hand:
                    // @custom-variant pointer-coarse (@media (pointer: coarse))
                    else if (directive == "custom-variant")
                    {
                        var splitAt = directiveParameter.IndexOf(' ');

                        if (splitAt == -1)
                        {
                            continue;
                        }

                        var variantName = directiveParameter.Substring(0, splitAt);
                        var variantValue = directiveParameter.Substring(splitAt + 1).Trim();

                        if (variantValue.StartsWith("(") && variantValue.EndsWith(")"))
                        {
                            variantValue = variantValue.Substring(1, variantValue.Length - 2);
                            variants[variantName] = $"{variantValue} {{ @slot; }}";
                        }

                        continue;
                    }
                    else if (directive == "source")
                    {
                        var not = false;

                        // @source not "../src/components/legacy";
                        // @source not inline("{hover:,focus:,}bg-red-{50,{100..900..100},950}");
                        if (directiveParameter.StartsWith("not"))
                        {
                            not = true;
                            directiveParameter = directiveParameter.Substring(3).Trim();
                        }

                        var firstQuote = directiveParameter.IndexOfAny(['\'', '"']);
                        var secondQuote = directiveParameter.IndexOfAny(['\'', '"'], firstQuote + 1);

                        if (firstQuote == -1 || secondQuote == -1)
                        {
                            continue;
                        }

                        var inQuotes = directiveParameter.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Trim();

                        // @source inline("{hover:,focus:,}bg-red-{50,{100..900..100},950}");
                        if (directiveParameter.StartsWith("inline("))
                        {
                            if (version >= TailwindVersion.V4_1)
                            {
                                blocklist = BraceExpander.Expand(inQuotes);
                            }
                        }
                        else
                        {
                            // @source "../src/components/legacy";
                            var source = PathHelpers.GetAbsolutePath(Path.GetDirectoryName(path)!, inQuotes)!;

                            if (not)
                            {
                                content.Add($"!{source}");
                            }
                            else
                            {
                                content.Add(source);
                            }
                        }
                    }

                    directive = "";
                    directiveParameter = "";
                }
                else if (current == '{' && !(directiveParameter.Count(p => p == '\'') == 1 || directiveParameter.Count(p => p == '"') == 1))
                {
                    // { is also a terminator for @plugin, so we'll handle that here
                    if (directive == "plugin")
                    {
                        var plugin = directiveParameter.Replace("\"", "").Replace("'", "").TrimEnd(';').Trim();

                        var pluginAbsPath = PathHelpers.GetAbsolutePath(Path.GetDirectoryName(path)!, plugin);

                        // If this path exists, then it is not a package name. If not, then we must resolve the package
                        if (!File.Exists(pluginAbsPath))
                        {
                            var resolvedPath = await ResolveFilePathFromNPMPackageNameAsync(path, plugin);
                            if (resolvedPath is not null)
                            {
                                imports.Add($"@plugin{resolvedPath}");
                            }
                        }
                        else
                        {
                            imports.Add($"@plugin{pluginAbsPath}");
                        }

                        continue;
                    }

                    directiveParameter = directiveParameter.Trim();
                    buildingDirectiveParameter = false;
                    level++;
                }
                else
                {
                    directiveParameter += current;
                }
                continue;
            }

            if (level >= 1)
            {
                if (directive == "theme" && level == 1 && current != '{' && current != '}')
                {
                    themeTrimmed.Append(current);
                }
                else if (directive == "utility")
                {
                    if (!utilities.ContainsKey(directiveParameter))
                    {
                        utilities[directiveParameter] = "";
                    }

                    utilities[directiveParameter] += current.ToString();
                }
                else if (directive == "custom-variant")
                {
                    if (!variants.ContainsKey(directiveParameter))
                    {
                        variants[directiveParameter] = "";
                    }

                    variants[directiveParameter] += current.ToString();
                }
            }
        }

        if (!isSourceSpecified)
        {
            // If no source(), the source path is the directory of the configuration file
            content.Add(Path.GetDirectoryName(path)!);
        }

        var themeValuePairs = CssConfigSplitter.Split(themeTrimmed.ToString())
            .Where(s =>
            {
                // Possible that there is a keyframes somewhere here, in that case ignore
                var split = s.IndexOf(':');
                return split != -1 && s.Trim().StartsWith("--");
            })
            .Select(s =>
            {
                var split = s.IndexOf(':');
                return new KeyValuePair<string, string>(s.Substring(0, split).Trim(), s.Substring(split + 1).Trim());
            });

        TailwindConfiguration? imported = null;

        foreach (var import in imports)
        {
            // Skips the @import or @config appended at the beginning
            var importPath = import.Substring(7);
            var prev = imported;

            if (import.StartsWith("@import"))
            {
                imported = await GetCssConfigurationAsync(importPath, version);
            }
            else if (import.StartsWith("@config"))
            {
                imported = await GetJavaScriptConfigurationAsync(importPath);
            }
            else if (import.StartsWith("@plugin"))
            {
                imported = await GetJavaScriptConfigurationAsync(importPath, true);
            }
            else
            {
                continue;
            }

            if (prev is not null)
            {
                DictionaryHelpers.MergeDictionaries(imported.OverridenValues, prev.OverridenValues);
                DictionaryHelpers.MergeDictionaries(imported.ExtendedValues, prev.ExtendedValues);
                DictionaryHelpers.MergeDictionaries(imported.ThemeVariables, prev.ThemeVariables);
                imported.PluginClasses.AddRange(prev.PluginClasses);
                imported.PluginVariants.AddRange(prev.PluginVariants);
                imported.Imports.AddRange(prev.Imports);
                imported.Blocklist ??= [.. blocklist];
                imported.Blocklist.AddRange(blocklist);
                DictionaryHelpers.MergeDictionaries(imported.PluginDescriptions, prev.PluginDescriptions);
                DictionaryHelpers.MergeDictionaries(imported.PluginVariantDescriptions, prev.PluginVariantDescriptions);
            }
        }

        if (imported is not null)
        {
            imported.PluginClasses = [.. imported.PluginClasses.Distinct()];
            imported.PluginVariants = [.. imported.PluginVariants.Distinct()];
            imported.Imports = [.. imported.Imports.Distinct()];
            if (imported.Blocklist is not null)
            {
                imported.Blocklist = [.. imported.Blocklist.Distinct()];
            }
        }

        var config = new TailwindConfiguration
        {
            OverridenValues = [],
            ExtendedValues = [],
            ThemeVariables = [],
            PluginClasses = [.. utilities.Keys],
            PluginVariants = [.. variants.Keys],
            PluginDescriptions = utilities.ToDictionary(v => v.Key, v => v.Value.Trim()),
            // For variants, put everything on the same line so it looks fine in completion tooltip
            PluginVariantDescriptions = variants.ToDictionary(v => v.Key, v =>
                string.Join(" ", v.Value.Split(new char[] { }, StringSplitOptions.RemoveEmptyEntries))),
            Imports = [.. imports.Select(i => i.Replace("@import", "").Replace("@config", ""))],
            ContentPaths = content,
            Blocklist = blocklist,
            Prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim()
        };

        var fontSizeCandidates = new Dictionary<string, Dictionary<string, object>>();

        foreach (var pair in themeValuePairs)
        {
            var stem = GetConfigurationClassStemFromCssVariable(pair.Key, version);
            var namespaceStem = GetCssVariableNamespace(pair.Key, version);

            if (stem is null || namespaceStem is null)
            {
                config.ThemeVariables[pair.Key.Trim()] = pair.Value.Trim();
                continue;
            }

            var valueKey = pair.Key.Trim().Substring(namespaceStem.Length);

            if (pair.Key.EndsWith("*"))
            {
                config.OverridenValues[stem] = new Dictionary<string, object>();
                config.ExtendedValues[stem] = new Dictionary<string, object>();
                continue;
            }

            Dictionary<string, object> dict;

            if (config.OverridenValues.ContainsKey(stem))
            {
                dict = (Dictionary<string, object>)config.OverridenValues[stem];
            }
            else
            {
                if (!config.ExtendedValues.ContainsKey(stem))
                {
                    config.ExtendedValues[stem] = new Dictionary<string, object>();
                }
                dict = (Dictionary<string, object>)config.ExtendedValues[stem];
            }

            // Special case
            // --text-tiny--line-height: 1.5rem; 
            // --text-tiny: 0.625rem;
            // --text-tiny--letter-spacing: 0.125rem; 
            // --text-tiny--font-weight: 500; 
            if (namespaceStem == "--text-")
            {
                var doubleDashIndex = valueKey.IndexOf("--");
                var value = doubleDashIndex == -1 ? valueKey : valueKey.Substring(0, doubleDashIndex);

                config.ThemeVariables[pair.Key.Trim()] = pair.Value.Trim();

                if (doubleDashIndex == -1)
                {
                    fontSizeCandidates.Remove(value);

                    if (dict.TryGetValue(value, out var obj1) && obj1 is object[] tuple && tuple.Length == 2)
                    {
                        tuple[0] = $"var({pair.Key.Trim()})";
                    }
                    else
                    {
                        dict[valueKey] = $"var({pair.Key.Trim()})";
                    }

                    continue;
                }

                Dictionary<string, string> fontSizeDict;
                if (dict.TryGetValue(value, out var obj))
                {
                    if (obj is object[] tuple && tuple.Length == 2 && tuple[1] is Dictionary<string, string> dictionary)
                    {
                        fontSizeDict = dictionary;
                    }
                    else if (obj is string val)
                    {
                        fontSizeDict = [];

                        dict[value] = new object[] { val, fontSizeDict };
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    fontSizeDict = [];
                    dict[value] = new object[] { "", fontSizeDict };

                    // At this point, we're uncertain if the fontSize class exists.
                    // What if the user defined --text-tiny--line-height but never defined --text-tiny?
                    fontSizeCandidates[value] = dict;
                }

                if (valueKey.EndsWith("--line-height"))
                {
                    fontSizeDict["lineHeight"] = $"var({pair.Key})";
                }
                else if (valueKey.EndsWith("--letter-spacing"))
                {
                    fontSizeDict["letterSpacing"] = $"var({pair.Key})";
                }
                else if (valueKey.EndsWith("-font-weight"))
                {
                    fontSizeDict["fontWeight"] = $"var({pair.Key})";
                }

                continue;
            }

            dict[valueKey] = pair.Value.Trim();
        }

        foreach (var candidate in fontSizeCandidates)
        {
            candidate.Value.Remove(candidate.Key);
        }

        if (imported is not null)
        {
            DictionaryHelpers.MergeDictionaries(config.OverridenValues, imported.OverridenValues);
            DictionaryHelpers.MergeDictionaries(config.ExtendedValues, imported.ExtendedValues);
            DictionaryHelpers.MergeDictionaries(config.ThemeVariables, imported.ThemeVariables);
            config.PluginClasses = [.. config.PluginClasses.Concat(imported.PluginClasses).Distinct()];
            config.PluginVariants = [.. config.PluginVariants.Concat(imported.PluginVariants).Distinct()];
            config.Imports = [.. config.Imports.Concat(imported.Imports).Distinct()];
            if (imported.Blocklist is not null)
            {
                if (config.Blocklist is not null)
                {
                    config.Blocklist = [.. config.Blocklist.Concat(imported.Blocklist).Distinct()];
                }
                else
                {
                    config.Blocklist = imported.Blocklist;
                }
            }
            DictionaryHelpers.MergeDictionaries(config.PluginDescriptions, imported.PluginDescriptions);
            DictionaryHelpers.MergeDictionaries(config.PluginVariantDescriptions, imported.PluginVariantDescriptions);
        }

        return config;
    }

    private static string? GetCssVariableNamespace(string variable, TailwindVersion version)
    {
        if (variable.StartsWith("--color-"))
        {
            return "--color-";
        }
        else if (variable.StartsWith("--font-weight-"))
        {
            return "--font-weight-";
        }
        else if (variable.StartsWith("--font-"))
        {
            return "--font-";
        }
        else if (variable.StartsWith("--text-shadow") && version >= TailwindVersion.V4_1)
        {
            return "--text-shadow";
        }
        else if (variable.StartsWith("--text-"))
        {
            return "--text-";
        }
        else if (variable.StartsWith("--tracking-"))
        {
            return "--tracking-";
        }
        else if (variable.StartsWith("--leading-"))
        {
            return "--leading-";
        }
        else if (variable.StartsWith("--breakpoint-"))
        {
            return "--breakpoint-";
        }
        else if (variable.StartsWith("--container-"))
        {
            // v4
            return "--container-";
        }
        else if (variable.StartsWith("--spacing-"))
        {
            return "--spacing-";
        }
        else if (variable.StartsWith("--radius-"))
        {
            return "--radius-";
        }
        else if (variable.StartsWith("--shadow-"))
        {
            return "--shadow-";
        }
        else if (variable.StartsWith("--inset-shadow-"))
        {
            // v4
            return "--inset-shadow-";
        }
        else if (variable.StartsWith("--drop-shadow-"))
        {
            return "--drop-shadow-";
        }
        else if (variable.StartsWith("--blur-"))
        {
            return "--blur-";
        }
        else if (variable.StartsWith("--perspective-"))
        {
            // v4
            return "--perspective-";
        }
        else if (variable.StartsWith("--aspect-"))
        {
            return "--aspect-";
        }
        else if (variable.StartsWith("--ease-"))
        {
            return "--ease-";
        }
        else if (variable.StartsWith("--animate-"))
        {
            return "--animate-";
        }

        return null;
    }

    private static string? GetConfigurationClassStemFromCssVariable(string variable, TailwindVersion version)
    {
        if (variable.StartsWith("--color-"))
        {
            return "colors";
        }
        else if (variable.StartsWith("--font-weight-"))
        {
            return "fontWeight";
        }
        else if (variable.StartsWith("--font-"))
        {
            return "fontFamily";
        }
        else if (variable.StartsWith("--text-shadow") && version >= TailwindVersion.V4_1)
        {
            // 4.1+
            return "v4_1-text-shadow";
        }
        else if (variable.StartsWith("--text-"))
        {
            return "fontSize";
        }
        else if (variable.StartsWith("--tracking-"))
        {
            return "letterSpacing";
        }
        else if (variable.StartsWith("--leading-"))
        {
            return "lineHeight";
        }
        else if (variable.StartsWith("--breakpoint-"))
        {
            return "screens";
        }
        else if (variable.StartsWith("--container-"))
        {
            // v4
            return "v4-container";
        }
        else if (variable.StartsWith("--spacing-"))
        {
            return "spacing";
        }
        else if (variable.StartsWith("--radius-"))
        {
            return "borderRadius";
        }
        else if (variable.StartsWith("--shadow-"))
        {
            return "boxShadow";
        }
        else if (variable.StartsWith("--inset-shadow-"))
        {
            // v4
            return "v4-insetShadow";
        }
        else if (variable.StartsWith("--drop-shadow-"))
        {
            return "dropShadow";
        }
        else if (variable.StartsWith("--blur-"))
        {
            return "blur";
        }
        else if (variable.StartsWith("--perspective-"))
        {
            // v4
            return "v4-perspective";
        }
        else if (variable.StartsWith("--aspect-"))
        {
            return "aspectRatio";
        }
        else if (variable.StartsWith("--ease-"))
        {
            return "transitionTimingFunction";
        }
        else if (variable.StartsWith("--animate-"))
        {
            return "animation";
        }

        return null;
    }

    private static async Task<TailwindConfiguration> GetJavaScriptConfigurationAsync(string path, bool isAPlugin = false)
    {
        var obj = await GetConfigJsonNodeAsync(path, isAPlugin);

        if (obj is null)
        {
            return new TailwindConfiguration
            {
                OverridenValues = [],
                ExtendedValues = []
            };
        }

        if (obj.Count == 1 && obj.ContainsKey("default"))
        {
            obj = obj["default"]!.AsObject();
        }

        var theme = obj["theme"];

        var plugins = GetTotalValue(obj["plugins"]) ?? [];

        var config = new TailwindConfiguration
        {
            OverridenValues = theme is null ? [] : GetTotalValue(theme, "extend") ?? [],
            ExtendedValues = theme is null ? [] : GetTotalValue(theme["extend"]) ?? [],
            Prefix = obj["prefix"]?.ToString(),
            ContentPaths = obj["content"] is null ? [] : JsonSerializer.Deserialize<List<string>>(obj["content"])!
        };

        try
        {
            config.PluginVariants = plugins.ContainsKey("variants") ? (List<string>)plugins["variants"] : [];

            config.PluginClasses = [];
            if (plugins.ContainsKey("classes"))
            {
                var classes = (List<string>)plugins["classes"];

                foreach (var item in classes)
                {
                    if (item.Contains("@media") || item.Contains("@font-face") || item.Contains("@keyframes") || item.Contains("@supports"))
                    {
                        continue;
                    }
                    var commaSplitClasses = item.Split(new string[] { ",", " .", "." }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var className in commaSplitClasses)
                    {
                        string toAdd = className.Trim();
                        if (toAdd.StartsWith("[") && toAdd.EndsWith("]"))
                        {
                            continue;
                        }
                        if (toAdd.Contains('[') && toAdd.IndexOf('[') != toAdd.IndexOf("-[") + 1)
                        {
                            toAdd = toAdd.Substring(0, toAdd.IndexOf('['));
                        }
                        if (toAdd.Contains(':'))
                        {
                            toAdd = toAdd.Substring(0, toAdd.IndexOf(':'));
                        }
                        if (toAdd.Contains(' '))
                        {
                            toAdd = toAdd.Substring(0, toAdd.IndexOf(' '));
                        }

                        toAdd = toAdd.TrimEnd(')');

                        if (config.PluginClasses.Contains(toAdd) == false && string.IsNullOrEmpty(toAdd) == false)
                        {
                            config.PluginClasses.Add(toAdd);
                        }
                    }
                }
            }

            if (obj["plugins"]?["descriptions"] is not null)
            {
                config.PluginDescriptions = JsonSerializer.Deserialize<Dictionary<string, string>>(obj["plugins"]!["descriptions"]!)!;
            }
        }
        catch (Exception ex)
        {
            await ex.LogAsync();
        }

        try
        {
            if (obj["blocklist"] is not null)
            {
                config.Blocklist = JsonSerializer.Deserialize<List<string>>(obj["blocklist"])!;
            }
        }
        catch (Exception ex)
        {
            await ex.LogAsync();
        }

        try
        {
            if (obj["corePlugins"] is not null)
            {
                if (GetValueKind(obj["corePlugins"]) == JsonValueKind.Array)
                {
                    config.EnabledCorePlugins = JsonSerializer.Deserialize<List<string>>(obj["corePlugins"])!;
                }
                else
                {
                    config.DisabledCorePlugins = [.. JsonSerializer.Deserialize<Dictionary<string, bool>>(obj["corePlugins"])!
                        .Where(kvp => !kvp.Value)
                        .Select(kvp => kvp.Key)];
                }
            }
        }
        catch (Exception ex)
        {
            await ex.LogAsync();
        }

        return config;
    }

    private static async Task<string?> GetNodeModulesFromConfigFilePathAsync(string configPath)
    {
        var file = await PhysicalFile.FromFileAsync(configPath);

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        // ContainingProject must be called on the UI thread
        if (file?.ContainingProject is null)
        {
            return null;
        }

        var project = file.ContainingProject;

        // Search for the node_modules folder in the project; start from the configuration file and
        // go up the directory tree (stopping at the project root) until the node_modules folder is found.

        var currentDirectory = Path.GetDirectoryName(configPath)?.ToLowerInvariant();
        var endDirectory = Path.GetDirectoryName(project.FullPath)?.ToLowerInvariant();

        if (string.IsNullOrEmpty(currentDirectory) || string.IsNullOrEmpty(endDirectory))
        {
            return null;
        }


        while (currentDirectory!.StartsWith(endDirectory))
        {
            var nodeModulesPath = Path.Combine(currentDirectory, "node_modules");

            if (Directory.Exists(nodeModulesPath))
            {
                return nodeModulesPath;
            }

            var parent = Path.GetDirectoryName(currentDirectory);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }
            currentDirectory = parent.ToLowerInvariant();
        }

        return null;
    }

    /// <summary>
    /// Resolves the absolute file path to the main entry point of an installed npm package.
    /// </summary>.
    /// <param name="workingDir">The directory in which to execute the Node.js process. This should be the root directory where the npm package
    /// is installed.</param>
    /// <param name="package">The name of the npm package to resolve. Must be a valid package name installed in the specified working
    /// directory.</param>
    /// <returns>A string containing the absolute file path to the main entry point of the specified npm package if resolution is
    /// successful; otherwise, null.</returns>
    private static async Task<string?> ResolveFilePathFromNPMPackageNameAsync(string configFileLocation, string package)
    {
        // Do a basic check to ensure the package name is valid (very very crude security measure)
        // Based roughly on ChatGPT and this: https://npm.io/package/validate-npm-package-name
        foreach (char c in package)
        {
            if ((!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '/' && c != '@' && c != '.') || char.IsUpper(c))
            {
                return null;
            }
        }

        var processInfo = new ProcessStartInfo()
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            FileName = "cmd",
            Arguments = $"/c node -e \"console.log(require.resolve('{package}'))\"",
            WorkingDirectory = Path.GetDirectoryName(configFileLocation)!
        };

        var localNodePath = await GetNodeModulesFromConfigFilePathAsync(configFileLocation);
        await SetupNodeProcessAsync(processInfo, localNodePath);

        using var process = Process.Start(processInfo)!;
        await process.WaitForExitAsync();

        var path = (await process.StandardOutput.ReadToEndAsync()).Trim();

        // Check to see if the resolution worked; if not, then this will return false since
        // the output is not a path
        if (Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            return path;
        }

        return null;
    }

    /// <summary>
    /// Configures the NODE_PATH environment variable for the specified Node.js process start information, including
    /// local and global package paths as appropriate.
    /// </summary>
    /// <remarks>This method appends the local and global package paths to the NODE_PATH environment variable
    /// if they are available. Existing values in NODE_PATH are preserved and extended as needed.</remarks>
    /// <param name="nodeProcess">The process start information for the Node.js process. The method updates its NODE_PATH environment variable.</param>
    /// <param name="localNodePath">The local directory path to include in the NODE_PATH environment variable. Can be null; if specified and the
    /// directory exists, it is added to NODE_PATH.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private static async Task SetupNodeProcessAsync(ProcessStartInfo nodeProcess, string? localNodePath)
    {
        var globalPath = await NpmHelpers.GetGlobalNpmRootAsync();

        var nodePathEnvironmentVariable = nodeProcess.EnvironmentVariables["NODE_PATH"];

        if (!string.IsNullOrWhiteSpace(nodePathEnvironmentVariable))
        {
            nodePathEnvironmentVariable += ";";
        }

        if (localNodePath is not null && Directory.Exists(localNodePath))
        {
            nodePathEnvironmentVariable += localNodePath + ";";
        }

        if (!string.IsNullOrWhiteSpace(globalPath))
        {
            nodePathEnvironmentVariable += globalPath + ";";
        }

        if (!string.IsNullOrWhiteSpace(nodePathEnvironmentVariable))
        {
            nodePathEnvironmentVariable = nodePathEnvironmentVariable.TrimEnd(';');
        }

        nodeProcess.EnvironmentVariables["NODE_PATH"] = nodePathEnvironmentVariable;
    }

    private static Dictionary<string, object>? GetTotalValue(JsonNode? node, string? ignoreKey = null)
    {
        if (node is null)
        {
            return null;
        }

        var result = new Dictionary<string, object>();

        if (GetValueKind(node) == JsonValueKind.Object)
        {
            foreach (var key in GetKeys(node))
            {
                if (key == ignoreKey)
                {
                    continue;
                }

                var value = node[key];

                var valueKind = GetValueKind(value);

                if (valueKind == JsonValueKind.Null || value is null)
                {
                    continue;
                }

                if (valueKind == JsonValueKind.Object)
                {
                    result[key] = GetTotalValue(value)!;
                }
                else if (valueKind == JsonValueKind.Array)
                {
                    result[key] = value.AsArray().Select(n => n?.ToString()).ToList();
                }
                else
                {
                    result[key] = value.ToString().Trim();
                }
            }
        }

        return result;
    }

    private static ICollection<string> GetKeys(JsonNode obj)
    {
        return ((IDictionary<string, JsonNode>)obj).Keys;
    }

    private static JsonValueKind GetValueKind(JsonNode? node)
    {
        if (node is JsonObject)
        {
            return JsonValueKind.Object;
        }
        else if (node is JsonArray)
        {
            return JsonValueKind.Array;
        }
        else if (node is null)
        {
            return JsonValueKind.Null;
        }

        var value = node.GetValue<JsonElement>();

        return value.ValueKind;
    }
}
