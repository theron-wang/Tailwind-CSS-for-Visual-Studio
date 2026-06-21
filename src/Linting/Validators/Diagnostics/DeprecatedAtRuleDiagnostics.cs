using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

[Export(typeof(DiagnosticsChecker))]
internal class DeprecatedAtRuleDiagnostics() : CssDiagnosticsChecker(ErrorType.DeprecatedAtRule)
{
    /// <summary>
    /// https://github.com/tailwindlabs/tailwindcss-intellisense/blob/main/packages/tailwindcss-language-service/src/diagnostics/getDeprecatedAtRuleDiagnostics.ts
    /// </summary>
    private static readonly Regex _regex = new(
        @"(?<=\s|^)(?<directive>@variant)\s+[-_a-zA-Z0-9]+\s+\([^;{}]+\)\s*[;\r\n]",
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
        var (start, text) = GetFullScopeWithoutCssComments(span);

        var errors = _regex.Matches(text);

        foreach (Match match in errors)
        {
            var directive = match.Groups["directive"];

            var errorSpan = span.Snapshot.CreateTrackingSpan(
                start + directive.Index,
                directive.Length,
                SpanTrackingMode.EdgeExclusive
            );

            yield return new Error
            {
                Span = errorSpan,
                ErrorMessage =
                    "'@variant' is deprecated for defining custom variants. Use '@custom-variant' instead.",
                ErrorType = ErrorType.DeprecatedAtRule,
                Suggestion = new()
                {
                    Message = "Replace with '@custom-variant'",
                    SuggestedFix =
                    [
                        new SuggestionFix
                        {
                            ApplicableTo = errorSpan,
                            Replacement = "@custom-variant",
                        },
                    ],
                },
            };
        }
    }
}
