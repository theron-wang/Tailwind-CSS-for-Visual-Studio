using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

[Export(typeof(DiagnosticsChecker))]
internal class UsedBlocklistClassDiagnostics()
    : DiagnosticsChecker(false, ErrorType.UsedBlocklistClass)
{
    protected override IEnumerable<Error> GetErrorsImpl(
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

        var offset = scope!.IndexOf(content!);
        var classesToLocations = GetClassPositions(
            startOfScope + offset,
            content!,
            splitClasses(content!).Select(m => m.Value)
        );

        foreach (var @class in classesToLocations.Keys)
        {
            if (projectCompletionValues.IsClassAllowed(@class))
            {
                continue;
            }

            var suggestion = new Suggestion()
            {
                Message =
                    classesToLocations[@class].Count > 1
                        ? $"Remove all instances of '{@class}'"
                        : $"Remove '{@class}'",
                SuggestedFix = classesToLocations[@class]
                    .Select(s =>
                    {
                        var replacementSpan = s;

                        if (s.Start > 0 && s.Snapshot[s.Start - 1] == ' ')
                        {
                            replacementSpan = new SnapshotSpan(s.Start - 1, s.Length + 1);
                        }
                        else if ((int)s.End < s.Snapshot.Length && s.Snapshot[s.End] == ' ')
                        {
                            replacementSpan = new SnapshotSpan(s.Start, s.Length + 1);
                        }

                        return new SuggestionFix
                        {
                            ApplicableTo = span.Snapshot.CreateTrackingSpan(
                                replacementSpan,
                                SpanTrackingMode.EdgeExclusive
                            ),
                            Replacement = "",
                        };
                    }),
            };

            foreach (var match in classesToLocations[@class])
            {
                if (shouldNotAddErrors(match))
                {
                    continue;
                }

                var errorSpan = span.Snapshot.CreateTrackingSpan(
                    match,
                    SpanTrackingMode.EdgeExclusive
                );

                yield return new Error
                {
                    Span = errorSpan,
                    ErrorMessage =
                        $"The class \"{@class}\" will not be generated as it has been blocklisted.",
                    ErrorType = ErrorType.UsedBlocklistClass,
                    Suggestion = suggestion,
                };
            }
        }
    }
}
