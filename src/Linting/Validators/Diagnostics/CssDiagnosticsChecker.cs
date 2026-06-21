using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Parsers;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

internal abstract class CssDiagnosticsChecker(ErrorType errorType)
    : DiagnosticsChecker(true, errorType)
{
    protected static (int start, string text) GetFullScopeWithoutCssComments(SnapshotSpan span)
    {
        int start = span.Snapshot.Length;
        int end = 0;

        foreach (var scope in CssParser.GetScopes(span))
        {
            if (scope.Start < start)
            {
                start = scope.Start;
            }

            if (scope.End > end)
            {
                end = scope.End;
            }
        }

        if (end < start)
        {
            return (span.Span.Start, CommentRemover.StripCssComments(span.GetText()));
        }

        return (start, CommentRemover.StripCssComments(span.Snapshot.GetText(start, end - start)));
    }
}
