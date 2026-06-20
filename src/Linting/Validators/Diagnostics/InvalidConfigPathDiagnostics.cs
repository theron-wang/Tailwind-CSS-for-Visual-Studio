using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

/// <summary>
/// https://github.com/tailwindlabs/tailwindcss-intellisense/blob/main/packages/tailwindcss-language-service/src/diagnostics/getInvalidConfigPathDiagnostics.ts
/// </summary>
[Export(typeof(DiagnosticsChecker))]
internal class InvalidConfigPathDiagnostics() : CssDiagnosticsChecker(ErrorType.InvalidConfigPath)
{
    [Import]
    private readonly ProjectConfigurationManager _projectConfigurationManager = null!;

    private static readonly Regex _themeRegex = new(
        @"(?<helper>theme|screen)\(\s*(?<path>[^)]+?)\s*\)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );

    protected override IEnumerable<Error> GetErrorsImpl(
        SnapshotSpan span,
        ProjectCompletionValues projectCompletionValues,
        Func<string, IEnumerable<Match>> findClasses,
        Func<string, IEnumerable<Match>> splitClasses,
        Func<SnapshotSpan, bool> shouldNotAddErrors
    )
    {
        var text = GetFullScopeWithoutCssComments(span);

        foreach (var match in _themeRegex.Matches(text).Cast<Match>())
        {
            var pathGroup = match.Groups["path"];
            var rawPath = pathGroup.Value.Trim().Trim('"', '\'').Trim();

            var errorSpan = span.Snapshot.CreateTrackingSpan(
                span.Span.Start + pathGroup.Index,
                pathGroup.Length,
                // Use edge inclusive here because suggestions with EdgeExclusive leaves a one-character space
                // where the error is not invalidated
                SpanTrackingMode.EdgeInclusive
            );

            if (projectCompletionValues.Version >= TailwindVersion.V4)
            {
                var (isValid, reason, suggestion) = ValidateV4ThemePath(
                    rawPath,
                    projectCompletionValues
                );
                if (!isValid)
                {
                    yield return new Error
                    {
                        Span = errorSpan,
                        ErrorMessage = reason!,
                        ErrorType = ErrorType.InvalidConfigPath,
                        Suggestion = suggestion is null
                            ? null
                            : new Suggestion
                            {
                                Message = $"Replace with '{suggestion}'",
                                SuggestedFix =
                                [
                                    new SuggestionFix
                                    {
                                        ApplicableTo = errorSpan,
                                        Replacement = suggestion,
                                    },
                                ],
                            },
                    };
                }
                continue;
            }

            var (v3IsValid, v3Reason) = ValidateV3ThemePath(rawPath, projectCompletionValues);
            if (!v3IsValid)
            {
                yield return new Error
                {
                    Span = errorSpan,
                    ErrorMessage = v3Reason!,
                    ErrorType = ErrorType.InvalidConfigPath,
                };
            }
        }
    }

    private (bool isValid, string? reason, string? closest) ValidateV4ThemePath(
        string path,
        ProjectCompletionValues projectCompletionValues
    )
    {
        if (path.Contains('.'))
        {
            var first = path.Split('.')[0];

            var replacement = ThemeVariableHelpers.GetCssVariableFromConfigurationClassStem(first);

            if (replacement is not null)
            {
                path = path.Replace(first, replacement.Trim('-'));
            }
        }

        var asCssVariable = path.StartsWith("--") ? path : $"--{path.Replace('.', '-')}";
        var color = asCssVariable.StartsWith("--color-")
            ? asCssVariable.Replace("--color-", "")
            : null;

        var notFirstSegment = string.Join("-", asCssVariable.Trim('-').Split('-').Skip(1));

        // V4 theme paths are CSS variables (--colors-red-500) or dot-notation (colors.red.500)
        // Attempt resolution against known theme keys
        if (
            projectCompletionValues.CssVariables.TryGetValue(asCssVariable, out _)
            || (color is not null && projectCompletionValues.ColorMapper.ContainsKey(color))
        )
        {
            return (true, null, null);
        }

        // theme(spacing.4) is valid
        if (asCssVariable.StartsWith("--spacing-") && int.TryParse(notFirstSegment, out _))
        {
            return (true, null, null);
        }

        var reason = path.StartsWith("--")
            ? $"'{path}' does not exist in your theme."
            : $"'{path}' does not exist in your theme config.";

        IEnumerable<string> candidates = projectCompletionValues.CssVariables.Keys;

        if (int.TryParse(notFirstSegment, out _))
        {
            candidates = candidates.Concat([$"--spacing-{notFirstSegment}"]);
        }
        else
        {
            candidates = candidates.Concat(
                projectCompletionValues.ColorMapper.Keys.Select(c => $"--color-{c}")
            );
        }

        var suggestion = asCssVariable.GetClosestString(candidates);

        if (suggestion is not null)
        {
            if (!path.StartsWith("--"))
            {
                suggestion = ThemeVariableHelpers.FormatConfigurationPathFromCssVariable(
                    suggestion,
                    projectCompletionValues.Version
                );
            }

            if (suggestion is not null)
            {
                reason += $" Did you mean '{suggestion}'?";
            }
        }

        return (false, reason, suggestion);
    }

    private (bool isValid, string? reason) ValidateV3ThemePath(
        string themeValue,
        ProjectCompletionValues projectCompletionValues
    )
    {
        var segments = TokenizeTheme(themeValue);

        if (segments.Count == 0 || segments.Any(string.IsNullOrWhiteSpace))
        {
            return (false, $"'{themeValue}' does not exist in your theme config.");
        }

        bool error = false;
        bool foundButInvalid = false;

        if (segments[0] == "colors")
        {
            error = !projectCompletionValues.ColorMapper.ContainsKey(
                string.Join("-", segments.Skip(1))
            );
        }
        else if (segments[0] == "spacing")
        {
            error = !projectCompletionValues.SpacingMapper.ContainsKey(
                string.Join("-", segments.Skip(1))
            );
        }
        else if (segments[0] == "screens")
        {
            error = !projectCompletionValues.Breakpoints.ContainsKey(
                string.Join("-", segments.Skip(1))
            );
        }
        else if (_projectConfigurationManager.Configuration.LastConfig is not null)
        {
            if (
                _projectConfigurationManager.Configuration.LastConfig.OverridenValues.TryGetValue(
                    segments[0],
                    out var config
                )
            )
            {
                for (int i = 1; i < segments.Count; i++)
                {
                    if (
                        config is not Dictionary<string, object> dict
                        || !dict.ContainsKey(segments[i])
                    )
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
            else if (
                projectCompletionValues.ConfigurationValueToClassStems.TryGetValue(
                    segments[0],
                    out var classTypes
                )
            )
            {
                if (segments.Count == 1)
                {
                    error = true;
                    foundButInvalid = true;
                }
                else
                {
                    var values = ResolveClassStemValues(classTypes[0], projectCompletionValues);

                    var compareTo = themeValue.Replace("[", ".").Replace("]", "");
                    compareTo = compareTo.Substring(compareTo.IndexOf('.') + 1);

                    if (!values.Any(v => compareTo.Equals(v.Replace('-', '.'))))
                    {
                        error = true;
                    }
                }
            }
            else
            {
                error = true;
            }

            if (
                _projectConfigurationManager.Configuration.LastConfig.ExtendedValues.TryGetValue(
                    segments[0],
                    out var extConfig
                )
            )
            {
                bool previouslyFound = foundButInvalid || !error;
                bool found = true;

                for (int i = 1; i < segments.Count; i++)
                {
                    if (
                        extConfig is not Dictionary<string, object> dict
                        || !dict.ContainsKey(segments[i])
                    )
                    {
                        found = false;
                        break;
                    }
                    extConfig = dict[segments[i]];
                }

                if (!previouslyFound)
                {
                    error = !found;
                }

                if (found && extConfig is Dictionary<string, object>)
                {
                    error = true;
                    foundButInvalid = true;
                }
            }
        }

        if (!error)
        {
            return (true, null);
        }

        var reason = foundButInvalid
            ? $"'{themeValue}' was found but does not resolve to a valid theme value."
            : $"'{themeValue}' does not exist in your theme config.";

        return (false, reason);
    }

    private static HashSet<string> ResolveClassStemValues(
        string classType,
        ProjectCompletionValues projectCompletionValues
    )
    {
        HashSet<string> values = [];

        if (classType.Contains("{s}"))
        {
            var key = classType.Replace("{s}", "{0}");
            var source = projectCompletionValues.CustomSpacingMappers.TryGetValue(
                key,
                out var spacing
            )
                ? spacing
                : projectCompletionValues.SpacingMapper;
            foreach (var s in source)
            {
                values.Add(s.Key);
            }
        }
        else if (classType.Contains("{c}"))
        {
            var key = classType.Replace("{c}", "{0}");
            var source = projectCompletionValues.CustomColorMappers.TryGetValue(key, out var colors)
                ? colors
                : projectCompletionValues.ColorMapper;
            foreach (var c in source)
            {
                values.Add(c.Key);
            }
        }
        else if (classType.Contains('{') && !classType.Contains("{*}"))
        {
            if (classType.Contains('!'))
            {
                var stem = classType.Substring(0, classType.IndexOf('{'));
                var excluded = classType
                    .Substring(
                        classType.IndexOf('{') + 2,
                        classType.IndexOf('}') - classType.IndexOf('{') - 2
                    )
                    .Split('|');
                foreach (
                    var value in projectCompletionValues.Classes.Where(c => c.Name.StartsWith(stem))
                )
                {
                    var toAdd = value.Name.Replace(stem, "");
                    if (!excluded.Contains(toAdd) && !string.IsNullOrWhiteSpace(toAdd))
                    {
                        values.Add(toAdd);
                    }
                }
            }
            else
            {
                foreach (
                    var value in classType
                        .Substring(
                            classType.IndexOf('{') + 1,
                            classType.IndexOf('}') - classType.IndexOf('{') - 1
                        )
                        .Split('|')
                )
                {
                    values.Add(value);
                }
            }
        }
        else if (classType.EndsWith(":"))
        {
            var variant = classType.Replace(':', '-');
            foreach (
                var value in projectCompletionValues.Variants.Where(m => m.StartsWith(variant))
            )
            {
                values.Add(value.Replace(variant, ""));
            }
        }
        else
        {
            var stem = classType.Replace("{*}", "");
            if (!stem.EndsWith("-"))
            {
                stem += '-';
            }

            foreach (
                var value in projectCompletionValues.Classes.Where(c =>
                    c.Name.StartsWith(classType)
                )
            )
            {
                if (value.UseSpacing)
                {
                    var key = classType.Replace("{s}", "{0}");
                    var source = projectCompletionValues.CustomSpacingMappers.TryGetValue(
                        key,
                        out var spacing
                    )
                        ? spacing
                        : projectCompletionValues.SpacingMapper;
                    foreach (var s in source)
                    {
                        values.Add(s.Key);
                    }
                }
                else if (value.UseColors)
                {
                    var key = classType.Replace("{c}", "{0}");
                    var source = projectCompletionValues.CustomColorMappers.TryGetValue(
                        key,
                        out var colors
                    )
                        ? colors
                        : projectCompletionValues.ColorMapper;
                    foreach (var c in source)
                    {
                        values.Add(c.Key);
                    }
                }
                else if (!value.HasArbitrary)
                {
                    values.Add(value.Name.Replace(stem, ""));
                }
            }
        }

        return values;
    }

    private static List<string> TokenizeTheme(string input)
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
