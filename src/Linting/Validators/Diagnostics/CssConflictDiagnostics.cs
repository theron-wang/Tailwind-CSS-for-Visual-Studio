using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

[Export(typeof(DiagnosticsChecker))]
internal class CssConflictDiagnostics() : DiagnosticsChecker(false, ErrorType.CssConflict)
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

        List<string> classes = [.. splitClasses(content!).Select(c => c.Value)];

        var classesByVariants = classes.GroupBy(c =>
        {
            var unescaped = c.Replace("@@", "@").Replace("@(\"@\")", "@");

            var index = unescaped.LastIndexOf(':');

            if (index == -1)
            {
                return "";
            }
            return string.Join(":", unescaped.Substring(0, index).Split(':').OrderBy(x => x));
        });

        var offset = scope!.IndexOf(content);
        var classesToLocations = GetClassPositions(startOfScope + offset, content!, classes);

        // Ensure that duplicates do not point to the same location
        var alreadySeen = new HashSet<SnapshotSpan>();
        foreach (var grouping in classesByVariants)
        {
            alreadySeen.Clear();
            foreach (
                (
                    var className,
                    var errorMessage,
                    var conflictingClasses
                ) in _linterUtilities.CheckForClassDuplicates(grouping, projectCompletionValues)
            )
            {
                var classSpans = classesToLocations[className];

                var errorSpan = classSpans.First(s => !alreadySeen.Contains(s));

                alreadySeen.Add(errorSpan);

                if (shouldNotAddErrors(errorSpan))
                {
                    continue;
                }

                yield return new Error
                {
                    Span = errorSpan.Snapshot.CreateTrackingSpan(
                        errorSpan,
                        SpanTrackingMode.EdgeExclusive
                    ),
                    ErrorMessage = errorMessage,
                    ErrorType = ErrorType.CssConflict,
                    Suggestion = new()
                    {
                        Message =
                            $"Delete {conflictingClasses.Select(c => $"'{c}'").JoinWithCommasAndAnd()}",
                        SuggestedFix = conflictingClasses
                            .SelectMany(c =>
                            {
                                if (c == className)
                                {
                                    // There are duplicates; we don't want to erase the original
                                    return classesToLocations[c].Skip(1);
                                }
                                return classesToLocations[c];
                            })
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
                    },
                };
            }
        }
    }
}
