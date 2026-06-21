using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

/// <summary>
/// https://github.com/tailwindlabs/tailwindcss-intellisense/blob/main/packages/tailwindcss-language-service/src/diagnostics/getInvalidScreenDiagnostics.ts
/// </summary>
[Export(typeof(DiagnosticsChecker))]
internal class InvalidScreenDiagnostics() : CssDiagnosticsChecker(ErrorType.InvalidScreen)
{
    private static readonly Regex _regex = new(
        @"(?<=\s|^)@screen\s+(?<screen>[^\s{]+)",
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

        foreach (var match in _regex.Matches(text).Cast<Match>())
        {
            var screenGroup = match.Groups["screen"];

            if (projectCompletionValues.Breakpoints.ContainsKey(screenGroup.Value))
            {
                continue;
            }

            var errorSpan = span.Snapshot.CreateTrackingSpan(
                start + screenGroup.Index,
                screenGroup.Length,
                SpanTrackingMode.EdgeExclusive
            );

            var closest = screenGroup.Value.GetClosestString(
                projectCompletionValues.Breakpoints.Keys
            );

            yield return new Error
            {
                Span = errorSpan,
                ErrorMessage =
                    $"The '{screenGroup.Value}' screen does not exist in your theme config."
                    + (closest is not null ? $" Did you mean '{closest}'?" : null),
                ErrorType = ErrorType.InvalidScreen,
                Suggestion = closest is not null
                    ? new Suggestion
                    {
                        Message = $"Replace with '{closest}'",
                        SuggestedFix =
                        [
                            new SuggestionFix { ApplicableTo = errorSpan, Replacement = closest },
                        ],
                    }
                    : null,
            };
        }
    }
}
