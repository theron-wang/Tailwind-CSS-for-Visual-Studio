using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;

namespace TailwindCSSIntellisense.Parsers;

internal static class RazorParser
{
    /// <summary>
    /// Returns true if the trigger is in a class scope (i.e. class="...")
    /// </summary>
    /// <param name="snapshot">The text snapshot</param>
    /// <param name="trigger">The trigger, such as the caret or the quick info location</param>
    /// <param name="fullClassScope">The output SnapshotSpan of the <b>entire</b> class scope</param>
    public static bool IsInClassScope(ITextSnapshot snapshot, SnapshotPoint trigger, out SnapshotSpan? fullClassScope)
    {
        var expandedSearchText = snapshot.GetText(0, Math.Min((int)trigger + 2000, snapshot.Length));

        foreach (var match in ClassRegexHelper.GetClassesRazor(expandedSearchText))
        {
            if (trigger.Position >= match.Index && trigger.Position <= match.Index + match.Length)
            {
                var group = ClassRegexHelper.GetClassTextGroup(match);

                fullClassScope = new SnapshotSpan(snapshot, group.Index, group.Length);

                return true;
            }
        }

        fullClassScope = null;
        return false;
    }

    /// <summary>
    /// Returns true if the cursor is in a class scope (i.e. class="...")
    /// </summary>
    /// <param name="textView">The text view</param>
    /// <param name="fullClassScope">The output SnapshotSpan of the <b>entire</b> class scope</param>
    public static bool IsCursorInClassScope(ITextView textView, out SnapshotSpan? fullClassScope)
    {
        return IsInClassScope(textView.TextSnapshot, textView.Caret.Position.BufferPosition, out fullClassScope);
    }

    /// <summary>
    /// Gets the class scopes that intersect with the given span. Includes class="..."
    /// </summary>
    public static IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span)
    {
        var start = Math.Max(0, (int)span.Start - 2000);

        foreach (var scope in ClassRegexHelper.GetClassesRazorEnumerator(span.Snapshot.GetText(start, Math.Min(span.Snapshot.Length, (int)span.End + 2000) - start)))
        {
            var potentialReturn = new SnapshotSpan(span.Snapshot, start + scope.Index, scope.Length);

            if (!potentialReturn.IntersectsWith(span))
            {
                continue;
            }

            yield return potentialReturn;
        }
    }

    /// <summary>
    /// Similar to GetScopes, but gets the class attribute values that intersect with the given span. Includes the ... in class="...", but not the class or quotation marks themselves.
    /// </summary>
    public static IEnumerable<SnapshotSpan> GetClassAttributeValues(SnapshotSpan span)
    {
        var start = Math.Max(0, (int)span.Start - 2000);

        foreach (var scope in ClassRegexHelper.GetClassesRazorEnumerator(span.Snapshot.GetText(start, Math.Min(span.Snapshot.Length, (int)span.End + 2000) - start)))
        {
            var text = ClassRegexHelper.GetClassTextGroup(scope);
            var potentialReturn = new SnapshotSpan(span.Snapshot, start + text.Index, text.Length);

            if (!potentialReturn.IntersectsWith(span))
            {
                continue;
            }

            yield return potentialReturn;
        }
    }

    /// <summary>
    /// Gets the class attribute value that intersect with the given point. Includes the ... in class="...", but not the class or quotation marks themselves.
    /// </summary>
    /// <param name="point">The point that is inside of a class context</param>
    public static SnapshotSpan? GetClassAttributeValue(SnapshotPoint point)
    {
        var start = Math.Max(0, (int)point - 2000);

        // We check the point before the trigger. In the case of a caret, the character after is what is selected.
        // For GetApplicableTo, we want what is before.
        var checkPoint = point == 0 ? point : point - 1;

        foreach (var scope in ClassRegexHelper.GetClassesRazorEnumerator(point.Snapshot.GetText(start, Math.Min(point.Snapshot.Length, (int)point + 2000) - start)))
        {
            var text = ClassRegexHelper.GetClassTextGroup(scope);
            var lower = start + scope.Index;
            var upper = lower + scope.Length;

            if (point < lower || point > upper)
            {
                continue;
            }

            foreach (var token in ClassRegexHelper.SplitRazorClasses(text.Value))
            {
                var potentialReturn = new SnapshotSpan(point.Snapshot, start + text.Index + token.Index, token.Length);

                if (!potentialReturn.Contains(checkPoint))
                {
                    continue;
                }

                return potentialReturn;
            }

            // Most likely a space
            return new SnapshotSpan(point, 0);
        }

        return null;
    }
}
