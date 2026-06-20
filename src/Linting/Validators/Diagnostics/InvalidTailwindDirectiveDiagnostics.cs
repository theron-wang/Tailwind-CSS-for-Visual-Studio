using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

/// <summary>
/// https://github.com/tailwindlabs/tailwindcss-intellisense/blob/main/packages/tailwindcss-language-service/src/diagnostics/getInvalidTailwindDirectiveDiagnostics.ts
/// </summary>
[Export(typeof(DiagnosticsChecker))]
internal class InvalidTailwindDirectiveDiagnostics()
    : CssDiagnosticsChecker(ErrorType.InvalidTailwindDirective)
{
    private static readonly Regex _regex = new(
        @"(?<=\s|^)@tailwind\s+(?<value>[^;\r\n]+)",
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

        foreach (var match in _regex.Matches(text).Cast<Match>())
        {
            var tailwindDirectiveGroup = match.Groups["value"];

            var tailwindDirective = tailwindDirectiveGroup.Value;

            var errorSpan = span.Snapshot.CreateTrackingSpan(
                span.Span.Start + tailwindDirectiveGroup.Index,
                tailwindDirectiveGroup.Length,
                SpanTrackingMode.EdgeExclusive
            );

            if (projectCompletionValues.Version >= TailwindVersion.V4)
            {
                if (tailwindDirective == "utilities")
                {
                    yield break;
                }

                var replacementSpan = span.Snapshot.CreateTrackingSpan(
                    span.Span.Start + match.Index,
                    match.Length,
                    SpanTrackingMode.EdgeExclusive
                );

                if (tailwindDirective == "base" || tailwindDirective == "preflight")
                {
                    yield return new Error
                    {
                        Span = errorSpan,
                        ErrorMessage =
                            $"'@tailwind {tailwindDirective}' is no longer available in v4. Use '@import \"tailwindcss/preflight\"' instead.",
                        ErrorType = ErrorType.InvalidTailwindDirective,
                        Suggestion = new Suggestion
                        {
                            Message = "Replace with '@import \"tailwindcss/preflight\"'",
                            SuggestedFix =
                            [
                                new SuggestionFix
                                {
                                    Replacement = "@import \"tailwindcss/preflight\"",
                                    ApplicableTo = replacementSpan,
                                },
                            ],
                        },
                    };
                    yield break;
                }

                if (
                    tailwindDirective == "components"
                    || tailwindDirective == "screens"
                    || tailwindDirective == "variants"
                )
                {
                    yield return new Error
                    {
                        Span = errorSpan,
                        ErrorMessage =
                            $"'@tailwind {tailwindDirective}' is no longer available in v4. Use '@tailwind utilities' instead.",
                        ErrorType = ErrorType.InvalidTailwindDirective,
                        Suggestion = new Suggestion
                        {
                            Message = "Replace with '@tailwind utilities'",
                            SuggestedFix =
                            [
                                new SuggestionFix
                                {
                                    Replacement = "@tailwind utilities",
                                    ApplicableTo = replacementSpan,
                                },
                            ],
                        },
                    };
                    yield break;
                }

                var parts = tailwindDirective.Split(
                    null as char[],
                    StringSplitOptions.RemoveEmptyEntries
                );
                if (parts.Length > 1 && parts[0] == "utilities" && parts[1].StartsWith("source("))
                {
                    yield break;
                }

                yield return new Error
                {
                    Span = errorSpan,
                    ErrorMessage = $"'{tailwindDirective}' is not a valid value.",
                    ErrorType = ErrorType.InvalidTailwindDirective,
                };
                yield break;
            }

            var valid = new List<string>
            {
                "utilities",
                "components",
                "screens",
                "base",
                "variants",
            };

            if (valid.Contains(tailwindDirective))
            {
                yield break;
            }

            var message = $"'{tailwindDirective}' is not a valid value.";

            if (tailwindDirective == "preflight")
            {
                message += " Did you mean 'base'?";
            }

            yield return new Error
            {
                Span = errorSpan,
                ErrorMessage = message,
                ErrorType = ErrorType.InvalidTailwindDirective,
                Suggestion = new Suggestion
                {
                    Message = "Replace with 'base'",
                    SuggestedFix =
                    [
                        new SuggestionFix { Replacement = "base", ApplicableTo = errorSpan },
                    ],
                },
            };
        }
    }
}
