using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Parsers;

namespace TailwindCSSIntellisense.Linting.Validators;

internal class CssValidator : Validator
{
    private CssValidator(ITextBuffer buffer, LinterUtilities linterUtils, ProjectConfigurationManager completionUtilities, CompletionConfiguration completionConfiguration) : base(buffer, linterUtils, completionUtilities, completionConfiguration)
    {

    }

    public override IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span)
    {
        return CssParser.GetScopes(span, span.Snapshot);
    }

    public override IEnumerable<Error> GetErrors(SnapshotSpan span, bool force = false)
    {
        if (_projectCompletionValues is null || (_checkedSpans.Contains(span) && !force))
        {
            yield break;
        }

        var text = span.GetText();
        #region Css conflict
        if (_linterUtils.GetErrorSeverity(ErrorType.CssConflict) != ErrorSeverity.None && HasOnlyOneDirective(text, "@apply"))
        {
            text = GetFullScope(span, text);

            var classes = text.Trim().TrimEnd(';').Split(new char[] { }, StringSplitOptions.RemoveEmptyEntries).ToList();

            classes.Remove("@apply");

            var classesByVariants = classes.GroupBy(c =>
            {
                var index = c.LastIndexOf(':');

                if (index == -1)
                {
                    return "";
                }
                return string.Join(":", c.Substring(0, index).Split(':').OrderBy(x => x));
            });

            foreach (var grouping in classesByVariants)
            {
                // Find duplicates, since we are parsing from left to right
                int index = -1;
                foreach ((var errorClass, var errorMessage) in _linterUtils.CheckForClassDuplicates(grouping, _projectCompletionValues))
                {
                    index = text.IndexOf(errorClass, index + 1);

                    if (index == -1)
                    {
                        index = text.IndexOf(errorClass, index + 1);
                    }

                    while (index != -1 && index + errorClass.Length < text.Length && !char.IsWhiteSpace(text[index + errorClass.Length]) && !";}".Contains(text[index + errorClass.Length]))
                    {
                        index = text.IndexOf(errorClass, index + 1);
                    }

                    if (index == -1)
                    {
                        continue;
                    }

                    var errorSpan = new SnapshotSpan(_buffer.CurrentSnapshot, span.Span.Start + index, errorClass.Length);

                    _checkedSpans.Add(span);
                    yield return new Error(errorSpan, errorMessage, ErrorType.CssConflict);
                }
            }
        }
        #endregion
        #region @screen
        else if (_projectCompletionValues.Version == TailwindVersion.V3 && _linterUtils.GetErrorSeverity(ErrorType.InvalidScreen) != ErrorSeverity.None && HasOnlyOneDirective(text, "@screen"))
        {
            text = GetFullScope(span, text);

            var screen = text.Replace("@screen", "").Trim().TrimEnd('{').TrimEnd();

            if (_projectCompletionValues.Breakpoints.ContainsKey(screen) == false)
            {
                var errorSpan = new SnapshotSpan(_buffer.CurrentSnapshot, span.Span.Start + text.IndexOf(screen, text.IndexOf("@screen") + 7), screen.Length);

                _checkedSpans.Add(span);
                yield return new Error(errorSpan, $"The '{screen}' screen does not exist in your theme.", ErrorType.InvalidScreen);
            }
        }
        else if (_linterUtils.GetErrorSeverity(ErrorType.InvalidScreen) != ErrorSeverity.None && HasOnlyOneDirective(text, "@media"))
        {
            text = GetFullScope(span, text);

            var query = text.Replace("@media", "").Trim().TrimEnd('{').TrimEnd();

            if (query.StartsWith("screen") && query.Contains('(') && string.IsNullOrWhiteSpace(query.Substring(0, query.IndexOf('('))))
            {
                string? screen;
                try
                {
                    screen = query.Substring(query.IndexOf('(') + 1, query.IndexOf(')') - query.IndexOf('(') - 1).Trim();
                }
                catch
                {
                    screen = null;
                }

                if (!string.IsNullOrWhiteSpace(screen) && _projectCompletionValues.Breakpoints.ContainsKey(screen!) == false)
                {
                    var errorSpan = new SnapshotSpan(_buffer.CurrentSnapshot, span.Span.Start + text.IndexOf(screen, text.IndexOf("screen") + 7), screen!.Length);

                    _checkedSpans.Add(span);
                    yield return new Error(errorSpan, $"The '{screen}' screen does not exist in your theme.", ErrorType.InvalidScreen);
                }
            }
        }
        #endregion
        #region @tailwind
        else if (_linterUtils.GetErrorSeverity(ErrorType.InvalidTailwindDirective) != ErrorSeverity.None && HasOnlyOneDirective(text, "@tailwind"))
        {
            text = GetFullScope(span, text);

            var tailwindDirective = text.Substring(text.IndexOf("@tailwind") + 9).Trim().TrimEnd(';').TrimEnd();

            var startIndex = text.IndexOf("@tailwind") + 9;

            List<string> valid = ["base", "components", "utilities", "screens", "variants"];

            if (!string.IsNullOrWhiteSpace(tailwindDirective))
            {
                if (valid.Contains(tailwindDirective) == false)
                {
                    var errorSpan = new SnapshotSpan(_buffer.CurrentSnapshot, span.Span.Start + text.IndexOf(tailwindDirective, startIndex), tailwindDirective.Length);

                    _checkedSpans.Add(span);
                    yield return new Error(errorSpan, $"'{tailwindDirective}' is not a valid value.", ErrorType.InvalidTailwindDirective);
                }
                else if (_projectCompletionValues.Version >= TailwindVersion.V4)
                {
                    var errorSpan = new SnapshotSpan(_buffer.CurrentSnapshot, span.Span.Start + text.IndexOf(tailwindDirective, startIndex), tailwindDirective.Length);
                    if (tailwindDirective == "base")
                    {
                        _checkedSpans.Add(span);
                        yield return new Error(errorSpan, "'@tailwind base' is no longer available in v4. Use '@import \"tailwindcss/preflight\"' instead.", ErrorType.InvalidTailwindDirective);
                    }
                    else if (tailwindDirective != "utilities")
                    {
                        _checkedSpans.Add(span);
                        yield return new Error(errorSpan, $"'@tailwind {tailwindDirective}' is no longer available in v4. Use '@tailwind utilities' instead.", ErrorType.InvalidTailwindDirective);
                    }
                }
            }
        }
        #endregion
        #region theme
        else if (_linterUtils.GetErrorSeverity(ErrorType.InvalidConfigPath) != ErrorSeverity.None && HasOnlyOneDirective(text, "theme") && text.IndexOf('(', text.IndexOf("theme")) > -1)
        {
            text = GetFullScope(span, text);

            var startIndex = text.IndexOf('(', text.IndexOf("theme")) + 1;
            var endIndex = text.IndexOf(')', startIndex);

            if (endIndex == -1)
            {
                endIndex = text.Length;
            }

            endIndex -= startIndex;

            var themeValue = text.Substring(startIndex, endIndex).Trim().TrimEnd(')', ';', '}').Trim().Trim('"', '\'').Trim();

            var segments = TokenizeTheme(themeValue);

            bool error = false;
            bool foundButInvalid = false;
            if (segments.Count == 0 || segments.Any(string.IsNullOrWhiteSpace))
            {
                error = true;
            }
            else if (segments[0] == "colors")
            {
                error = !_projectCompletionValues.ColorMapper.ContainsKey(string.Join("-", segments.Skip(1)));
            }
            else if (segments[0] == "spacing")
            {
                error = !_projectCompletionValues.SpacingMapper.ContainsKey(string.Join("-", segments.Skip(1)));
            }
            else if (segments[0] == "screens")
            {
                error = !_projectCompletionValues.Breakpoints.ContainsKey(string.Join("-", segments.Skip(1)));
            }
            else if (_projectConfigurationManager.Configuration.LastConfig is not null)
            {
                if (_projectConfigurationManager.Configuration.LastConfig.OverridenValues.TryGetValue(segments[0], out var config))
                {
                    for (int i = 1; i < segments.Count; i++)
                    {
                        if (config is not Dictionary<string, object> dict || !dict.ContainsKey(segments[i]))
                        {
                            error = true;
                            break;
                        }
                        config = dict[segments[i]];
                    }

                    if (!error && config is Dictionary<string, object>)
                    {
                        error = true;
                        foundButInvalid = true;
                    }
                }
                else if (_projectCompletionValues.ConfigurationValueToClassStems.TryGetValue(segments[0], out var classTypes))
                {
                    if (segments.Count == 1)
                    {
                        error = true;
                        foundButInvalid = true;
                    }
                    else
                    {
                        HashSet<string> values = [];

                        var classType = classTypes[0];
                        if (classType.Contains("{s}"))
                        {
                            if (_projectCompletionValues.CustomSpacingMappers.TryGetValue(classType.Replace("{s}", "{0}"), out var spacing))
                            {
                                foreach (var s in spacing)
                                {
                                    values.Add(s.Key);
                                }
                            }
                            else
                            {
                                foreach (var s in _projectCompletionValues.SpacingMapper)
                                {
                                    values.Add(s.Key);
                                }
                            }
                        }
                        else if (classType.Contains("{c}"))
                        {
                            if (_projectCompletionValues.CustomColorMappers.TryGetValue(classType.Replace("{c}", "{0}"), out var colors))
                            {
                                foreach (var c in colors)
                                {
                                    values.Add(c.Key);
                                }
                            }
                            else
                            {
                                foreach (var c in _projectCompletionValues.ColorMapper)
                                {
                                    values.Add(c.Key);
                                }
                            }
                        }
                        else if (classType.Contains('{') && classType.Contains("{*}") == false)
                        {
                            if (classType.Contains('!'))
                            {
                                var stem = classType.Substring(0, classType.IndexOf('{'));
                                var excluded = classType.Substring(classType.IndexOf('{') + 2, classType.IndexOf('}') - classType.IndexOf('{') - 2).Split('|');
                                foreach (var value in _projectCompletionValues.Classes.Where(c => c.Name.StartsWith(stem)))
                                {
                                    var toAdd = value.Name.Replace(stem, "");

                                    if (excluded.Contains(toAdd) == false && string.IsNullOrWhiteSpace(toAdd) == false)
                                    {
                                        values.Add(toAdd);
                                    }
                                }
                            }
                            else
                            {
                                foreach (var value in classType.Substring(classType.IndexOf('{') + 1, classType.IndexOf('}') - classType.IndexOf('{') - 1).Split('|'))
                                {
                                    values.Add(value);
                                }
                            }
                        }
                        else if (classType.EndsWith(":"))
                        {
                            var variant = classType.Replace(':', '-');

                            foreach (var value in _projectCompletionValues.Variants.Where(m => m.StartsWith(variant)))
                            {
                                values.Add(value.Replace(variant, ""));
                            }
                        }
                        else
                        {
                            var stem = classType.Replace("{*}", "");

                            if (stem.EndsWith("-") == false)
                            {
                                stem += '-';
                            }

                            foreach (var value in _projectCompletionValues.Classes.Where(c => c.Name.StartsWith(classType)))
                            {
                                if (value.UseSpacing)
                                {
                                    if (_projectCompletionValues.CustomSpacingMappers.TryGetValue(classType.Replace("{s}", "{0}"), out var spacing))
                                    {
                                        foreach (var s in spacing)
                                        {
                                            values.Add(s.Key);
                                        }
                                    }
                                    else
                                    {
                                        foreach (var s in _projectCompletionValues.SpacingMapper)
                                        {
                                            values.Add(s.Key);
                                        }
                                    }
                                }
                                else if (value.UseColors)
                                {
                                    if (_projectCompletionValues.CustomColorMappers.TryGetValue(classType.Replace("{c}", "{0}"), out var colors))
                                    {
                                        foreach (var c in colors)
                                        {
                                            values.Add(c.Key);
                                        }
                                    }
                                    else
                                    {
                                        foreach (var c in _projectCompletionValues.ColorMapper)
                                        {
                                            values.Add(c.Key);
                                        }
                                    }
                                }
                                else if (value.HasArbitrary == false)
                                {
                                    values.Add(value.Name.Replace(stem, ""));
                                }
                            }
                        }

                        // Convert to dot format and see if they are equal; this is to show an error when
                        // the user may type in backgroundColor.green-500 instead of backgroundColor.green.500
                        var compareTo = themeValue.Replace("[", ".").Replace("]", "");
                        compareTo = compareTo.Substring(compareTo.IndexOf('.') + 1);
                        if (values.Any(v => compareTo.Equals(v.Replace('-', '.'))) == false)
                        {
                            error = true;
                        }
                    }
                }
                else
                {
                    error = true;
                }

                if (_projectConfigurationManager.Configuration.LastConfig.ExtendedValues.TryGetValue(segments[0], out config))
                {
                    bool previouslyFound = foundButInvalid || !error;

                    bool found = true;
                    for (int i = 1; i < segments.Count; i++)
                    {
                        if (config is not Dictionary<string, object> dict || !dict.ContainsKey(segments[i]))
                        {
                            found = false;
                            break;
                        }
                        config = dict[segments[i]];
                    }

                    if (!previouslyFound)
                    {
                        error = !found;
                    }

                    if (found && config is Dictionary<string, object>)
                    {
                        error = true;
                        foundButInvalid = true;
                    }
                }
            }

            if (error)
            {
                string errorMessage;
                if (foundButInvalid)
                {
                    errorMessage = $"'{themeValue}' was found but does not resolve to a valid theme value.";
                }
                else
                {
                    errorMessage = $"'{themeValue}' does not exist in your theme config.";
                }
                var errorSpan = new SnapshotSpan(_buffer.CurrentSnapshot, span.Span.Start + startIndex - 6, endIndex + 6);


                _checkedSpans.Add(span);
                yield return new Error(errorSpan, errorMessage, ErrorType.InvalidConfigPath);
            }
        }
        #endregion
    }

    public static Validator Create(ITextBuffer buffer, LinterUtilities linterUtils, ProjectConfigurationManager completionUtilities, CompletionConfiguration completionConfiguration)
    {
        return buffer.Properties.GetOrCreateSingletonProperty<Validator>(() => new CssValidator(buffer, linterUtils, completionUtilities, completionConfiguration));
    }

    private bool HasOnlyOneDirective(string text, string directive)
    {
        return text.Contains(directive) && text.IndexOf(directive, text.IndexOf(directive) + 1) == -1;
    }

    private string GetFullScope(SnapshotSpan span, string text)
    {
        var start = span.Span.Start;
        var length = span.Span.Length;

        while (start + length < span.Snapshot.Length && text.IndexOfAny([';', '{', '}']) == -1)
        {
            length++;
            text = span.Snapshot.GetText(start, length);
        }

        return text;
    }

    private List<string> TokenizeTheme(string input)
    {
        List<string> segments = [];
        int startIndex = 0;

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '[')
            {
                int endIndex = input.IndexOf(']', i);

                if (endIndex != -1)
                {
                    string segment = input.Substring(startIndex, i - startIndex).Trim();
                    if (!string.IsNullOrEmpty(segment))
                    {
                        segments.Add(segment);
                    }

                    segment = input.Substring(i, endIndex - i + 1).Trim('[', ']');
                    segments.Add(segment);

                    startIndex = endIndex + 1;
                    i = endIndex;
                }
            }
            else if (input[i] == '.')
            {
                string segment = input.Substring(startIndex, i - startIndex).Trim();
                if (!string.IsNullOrEmpty(segment))
                {
                    segments.Add(segment);
                }

                startIndex = i + 1;
            }
        }

        string lastSegment = input.Substring(startIndex).Trim();
        if (!string.IsNullOrEmpty(lastSegment))
        {
            segments.Add(lastSegment);
        }

        return segments;
    }
}
