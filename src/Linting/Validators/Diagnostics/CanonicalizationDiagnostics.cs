using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Linting.Validators.Canonicalization;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

[Export(typeof(DiagnosticsChecker))]
internal class CanonicalizationDiagnostics()
    : DiagnosticsChecker(false, ErrorType.SuggestCanonicalClasses)
{
    [Import]
    private Canonicalizer _canonicalizer = null!;

    protected override async IAsyncEnumerable<Error> GetErrorsImplAsync(
        SnapshotSpan span,
        ProjectCompletionValues projectCompletionValues,
        Func<string, IEnumerable<Match>> findClasses,
        Func<string, IEnumerable<Match>> splitClasses,
        Func<SnapshotSpan, bool> shouldNotAddErrors
    )
    {
        (var startOfScope, var scope, var content) = GetFullScope(span, findClasses);

        if (string.IsNullOrEmpty(scope) || string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        List<string> classes = [.. splitClasses(content!).Select(c => c.Value)];

        var offset = scope!.IndexOf(content);
        var classesToLocations = GetClassPositions(startOfScope + offset, content!, classes);

        var canonicalizations = await _canonicalizer.CanonicalizeAsync(
            classes,
            span.Snapshot.TextBuffer.GetFileNameSafe()
        );

        foreach (var canonicalization in canonicalizations)
        {
            var locations = classesToLocations
                .Where(c => canonicalization.ClassNames.Contains(c.Key))
                .SelectMany(c => c.Value.Select(v => (className: c.Key, location: v)))
                .Where(tuple => !shouldNotAddErrors(tuple.location))
                .ToList();

            var oldText = string.Join(" ", canonicalization.ClassNames);

            foreach (var (className, location) in locations)
            {
                yield return new Error
                {
                    Span = span.Snapshot.CreateTrackingSpan(
                        location,
                        SpanTrackingMode.EdgeInclusive
                    ),
                    ErrorMessage =
                        $"'{oldText}' can be replaced with '{canonicalization.CanonicalizedClassName}'",
                    ErrorType = ErrorType.SuggestCanonicalClasses,
                    Suggestion = new Suggestion
                    {
                        Message =
                            $"Replace '{oldText}' with '{canonicalization.CanonicalizedClassName}'",
                        SuggestedFix = locations
                            .Select(s => s.location)
                            .Select(s =>
                            {
                                var replacementSpan = s;

                                if (s != location)
                                {
                                    if (s.Start > 0 && s.Snapshot[s.Start - 1] == ' ')
                                    {
                                        replacementSpan = new SnapshotSpan(
                                            s.Start - 1,
                                            s.Length + 1
                                        );
                                    }
                                    else if (
                                        (int)s.End < s.Snapshot.Length
                                        && s.Snapshot[s.End] == ' '
                                    )
                                    {
                                        replacementSpan = new SnapshotSpan(s.Start, s.Length + 1);
                                    }
                                }

                                return new SuggestionFix
                                {
                                    ApplicableTo = span.Snapshot.CreateTrackingSpan(
                                        replacementSpan,
                                        SpanTrackingMode.EdgeExclusive
                                    ),
                                    Replacement =
                                        s == location
                                            ? canonicalization.CanonicalizedClassName
                                            : "",
                                };
                            }),
                    },
                };
            }
        }
    }
}
