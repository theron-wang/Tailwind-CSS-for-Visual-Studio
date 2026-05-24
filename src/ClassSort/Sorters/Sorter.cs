using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.ClassSort.Sorters;

internal abstract class Sorter
{
    [Import]
    public ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;
    [Import]
    public ClassSortUtilities ClassSortUtilities { get; set; } = null!;

    public abstract string[] Handled { get; }

    public async Task<string> SortAsync(string filePath, string input)
    {
        var output = new StringBuilder();

        await foreach (var segment in GetSegmentsAsync(filePath, input))
        {
            output.Append(segment);
        }

        return output.ToString();
    }

    protected abstract IAsyncEnumerable<string> GetSegmentsAsync(string filePath, string input);

    protected async Task<string> SortSegmentAsync(string classText, string filePath)
    {
        var classes = classText.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);

        var sorted = await SortAsync(classes, filePath);

        var newlines = classText.Select((c, i) => (c, i))
            .Where(p => p.c == '\n')
            .Select(p => p.i).ToList();

        var indents = newlines.Select(i =>
        {
            var result = "";

            for (++i; i < classText.Length; i++)
            {
                if (!char.IsWhiteSpace(classText[i]))
                {
                    break;
                }
                result += classText[i];
            }
            return result;
        }).ToList();

        var sortedSegment = new StringBuilder();

        int index = 0;
        int nextNewLineIndex = 0;

        var shouldMoveImportant = false;
        foreach (var sortedClass in sorted)
        {
            if (this is CssSorter && sortedClass == "!important")
            {
                shouldMoveImportant = true;
                continue;
            }

            if (nextNewLineIndex < newlines.Count &&
                index <= newlines[nextNewLineIndex] && newlines[nextNewLineIndex] <= index + sortedClass.Length)
            {
                sortedSegment.AppendLine().Append(indents[nextNewLineIndex]).Append(sortedClass);
                index += indents[nextNewLineIndex].Length;

                nextNewLineIndex++;
            }
            else
            {
                sortedSegment.Append(' ').Append(sortedClass);
            }

            index += sortedClass.Length + 1;
        }

        if (shouldMoveImportant)
        {
            sortedSegment.Append(" !important");
        }

        return sortedSegment.ToString().Trim();
    }

    protected async Task<IOrderedEnumerable<string>> SortAsync(IEnumerable<string> classes, string filePath)
    {
        var projectCompletionValues = await ProjectConfigurationManager.GetCompletionConfigurationByFilePathAsync(filePath);

        var classOrder = await ClassSortUtilities.GetClassOrderAsync(projectCompletionValues);
        var variantOrder = await ClassSortUtilities.GetVariantOrderAsync(projectCompletionValues);

        var result = classes
            .OrderBy(className =>
            {
                var count = className.Count(c => c == ':');

                if (count > 0 && className.StartsWith("group-"))
                {
                    return 0;
                }

                return count;
            })
            .ThenBy(className =>
            {
                if (projectCompletionValues.Version >= TailwindVersion.V4)
                {
                    if (!string.IsNullOrWhiteSpace(projectCompletionValues.Prefix) && className.StartsWith(projectCompletionValues.Prefix))
                    {
                        className = className.Substring(projectCompletionValues.Prefix!.Length);
                    }
                }

                if (className.Contains(':'))
                {
                    var variants = className.Split(':');
                    if (projectCompletionValues.Version >= TailwindVersion.V4)
                    {
                        int max = -1;
                        foreach (var variant in variants)
                        {
                            if (variantOrder.TryGetValue(variant, out int index))
                            {
                                max = Math.Max(max, index);
                            }

                            var potentialBreakpointOrContainer = variant.Split('-').Last().Trim('@');

                            if (projectCompletionValues.Breakpoints.TryGetValue(potentialBreakpointOrContainer, out var breakpoint) &&
                                variantOrder.TryGetValue(variant.Replace(potentialBreakpointOrContainer, "{b}"), out index))
                            {
                                max = Math.Max(max, index + projectCompletionValues.Breakpoints.Count(b => CssSizeConverter.CssSizeToPixels(b.Value) < CssSizeConverter.CssSizeToPixels(breakpoint)));
                            }

                            if (projectCompletionValues.Containers.TryGetValue(potentialBreakpointOrContainer, out var container) &&
                                variantOrder.TryGetValue(variant.Replace(potentialBreakpointOrContainer, "{c}"), out index))
                            {
                                max = Math.Max(max, index + projectCompletionValues.Containers.Count(c => CssSizeConverter.CssSizeToPixels(c.Value) < CssSizeConverter.CssSizeToPixels(container)));
                            }

                            if (potentialBreakpointOrContainer.StartsWith("[") && potentialBreakpointOrContainer.EndsWith("]") &&
                                variantOrder.TryGetValue(variant.Replace(potentialBreakpointOrContainer, "{a}"), out index))
                            {
                                max = Math.Max(max, index);
                            }
                        }

                        if (max > -1)
                        {
                            return max;
                        }
                    }
                    else
                    {
                        int max = -1;

                        foreach (var variant in variants)
                        {
                            var num = -1;
                            var searchVariant = variant.Replace("group-", "").Replace("peer-", "");

                            // Normal --> group --> peer --> screen
                            if (variantOrder.TryGetValue(variant, out int index))
                            {
                                num = index;
                            }
                            else if (variantOrder.TryGetValue(searchVariant, out index))
                            {
                                if (variant.StartsWith("group-"))
                                {
                                    num = variantOrder.Count * 1000 + index;
                                }
                                else if (variant.StartsWith("peer-"))
                                {
                                    num = variantOrder.Count * 2000 + index;
                                }
                            }
                            else if (projectCompletionValues.Breakpoints.TryGetValue(variant, out var breakpoint))
                            {
                                num = variantOrder.Count * 3000 + projectCompletionValues.Breakpoints.Count(b => CssSizeConverter.CssSizeToPixels(b.Value) < CssSizeConverter.CssSizeToPixels(breakpoint));
                            }

                            max = Math.Max(max, num);
                        }

                        if (max > -1)
                        {
                            return max;
                        }
                    }
                    return int.MaxValue;
                }
                return 0;
            })
            .ThenBy(className =>
            {
                if (ImportantModifierHelper.IsImportantModifier(className))
                {
                    className = className.Trim('!');
                }

                var classToSearch = className;

                if (projectCompletionValues.Version >= TailwindVersion.V4)
                {
                    if (!string.IsNullOrWhiteSpace(projectCompletionValues.Prefix) && className.StartsWith(projectCompletionValues.Prefix))
                    {
                        classToSearch = className.Substring(projectCompletionValues.Prefix!.Length);
                    }
                }

                if (projectCompletionValues.Version == TailwindVersion.V3 && string.IsNullOrWhiteSpace(projectCompletionValues.Prefix) == false)
                {
                    classToSearch = classToSearch
                        .TrimPrefix(projectCompletionValues.Prefix, StringComparison.InvariantCultureIgnoreCase)
                        .TrimPrefix("-" + projectCompletionValues.Prefix, StringComparison.InvariantCultureIgnoreCase);

                    if (className.StartsWith("-"))
                    {
                        classToSearch = $"-{classToSearch}";
                    }

                    // Prefix not used, don't sort
                    if (classToSearch == className)
                    {
                        return -1;
                    }
                }

                classToSearch = classToSearch.Split(':').Last();

                if (classToSearch.Contains('-'))
                {
                    var ending = classToSearch.Split('-').Last();
                    var stem = classToSearch.Substring(0, classToSearch.LastIndexOf('-'));

                    var shouldKeepLooking = false;

                    if (projectCompletionValues.CustomSpacingMappers.TryGetValue($"{stem}-{{0}}", out var mapper))
                    {
                        if (mapper.ContainsKey(ending))
                        {
                            classToSearch = $"{stem}-{{s}}";
                        }

                        // While it may not necessarily be inside the custom color mapper, the class could still be valid
                        // For example: bg-inherit; any unfound classes will be handled in the final conditional return
                    }
                    // For V4, the later check is more comprehensive
                    else if (projectCompletionValues.Version == TailwindVersion.V3 && projectCompletionValues.SpacingMapper.ContainsKey(ending) ||
                        (
                            ending.Length > 2 &&
                            ending[0] == '[' &&
                            ending[ending.Length - 1] == ']' &&
                            classOrder.ContainsKey($"{stem}-{{s}}")
                        ))
                    {
                        if (!classOrder.ContainsKey(classToSearch))
                        {
                            classToSearch = $"{stem}-{{s}}";
                        }
                    }
                    else if (projectCompletionValues.CustomColorMappers.TryGetValue($"{stem}-{{0}}", out mapper))
                    {
                        if (mapper.ContainsKey(ending))
                        {
                            classToSearch = $"{stem}-{{c}}";
                        }

                        // While it may not necessarily be inside the custom color mapper, the class could still be valid
                        // For example: bg-inherit; any unfound classes will be handled in the final conditional return
                    }
                    else if (projectCompletionValues.ColorMapper.ContainsKey(ending.Split('/')[0]) ||
                            (
                                ending.Length > 2 &&
                                ending[0] == '[' &&
                                ending[ending.Length - 1] == ']' &&
                                classOrder.ContainsKey($"{stem}-{{c}}")
                            ))
                    {
                        classToSearch = $"{stem}-{{c}}";
                    }
                    else
                    {
                        if (stem.Contains('-'))
                        {
                            var splitIndex = classToSearch.LastIndexOf('-', classToSearch.LastIndexOf('-') - 1);
                            var colorStem = classToSearch.Substring(0, splitIndex);
                            var colorEnding = classToSearch.Substring(splitIndex + 1);

                            if (colorEnding.Contains('/'))
                            {
                                colorEnding = colorEnding.Substring(0, colorEnding.IndexOf('/'));
                            }

                            if (projectCompletionValues.CustomColorMappers.TryGetValue($"{colorStem}-{{0}}", out mapper))
                            {
                                if (mapper.ContainsKey(colorEnding))
                                {
                                    classToSearch = $"{colorStem}-{{c}}";
                                }
                                else
                                {
                                    shouldKeepLooking = true;
                                }

                                // While it may not necessarily be inside the custom color mapper, the class could still be valid
                                // For example: bg-inherit; any unfound classes will be handled in the final conditional return
                            }
                            else if (projectCompletionValues.ColorMapper.ContainsKey(colorEnding))
                            {
                                classToSearch = $"{colorStem}-{{c}}";
                            }
                            else
                            {
                                shouldKeepLooking = true;
                            }
                        }
                        else
                        {
                            shouldKeepLooking = true;
                        }
                    }

                    if (shouldKeepLooking)
                    {
                        if (projectCompletionValues.Version >= TailwindVersion.V4)
                        {
                            if (ending == "px")
                            {
                                classToSearch = $"{stem}-{{s}}";
                            }
                            else if (double.TryParse(ending, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                            {
                                classToSearch = $"{stem}-{{s}}";

                                if (!classOrder.TryGetValue(classToSearch, out _))
                                {
                                    classToSearch = $"{stem}-{{n}}";
                                }
                            }
                            else if (ending.EndsWith("%") && int.TryParse(ending.TrimEnd('%'), out _))
                            {
                                classToSearch = $"{stem}-{{%}}";
                            }
                            else if (ending.Count(c => c == '/') == 1 && int.TryParse(ending.Replace("/", ""), out _))
                            {
                                classToSearch = $"{stem}-{{f}}";
                            }
                            else if ((classToSearch.Contains('[') || classToSearch.Contains('(')))
                            {
                                classToSearch = $"{classToSearch.Substring(0, classToSearch.IndexOfAny(['[', '('])).Trim('-')}-{{a}}";
                            }
                        }
                        else if (ending.Length > 2 && ending[0] == '[' && ending[ending.Length - 1] == ']')
                        {
                            // We'll approximate the class here. For example, if we do rounded-[1.2rem],
                            // find rounded instead
                            classToSearch = stem;
                        }
                    }
                }

                if (!projectCompletionValues.IsClassAllowed(className))
                {
                    return -1;
                }

                return classOrder.TryGetValue(classToSearch, out int index) ? index : -1;
            });

        return result;
    }

    protected (int index, char terminator) GetNextIndexOfClass(string file, int startIndex, string searchFor = "class=")
    {
        var single = file.IndexOf($"{searchFor}'", startIndex, StringComparison.InvariantCultureIgnoreCase);
        var doubleQuote = file.IndexOf($"{searchFor}\"", startIndex, StringComparison.InvariantCultureIgnoreCase);

        if (single == -1 && doubleQuote == -1)
        {
            return (-1, ' ');
        }
        else if (single == -1)
        {
            return (doubleQuote, '"');
        }
        else if (doubleQuote == -1)
        {
            return (single, '\'');
        }
        else if (single < doubleQuote)
        {
            return (single, '\'');
        }
        else
        {
            return (doubleQuote, '"');
        }
    }
}
