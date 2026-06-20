using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.QuickInfo;

internal class CssQuickInfoSource(
    ITextBuffer textBuffer,
    DescriptionGenerator descriptionGenerator,
    ProjectConfigurationManager completionUtilities,
    SettingsProvider settingsProvider
) : QuickInfoSource(textBuffer, descriptionGenerator, completionUtilities, settingsProvider)
{
    protected override bool IsInClassScope(IAsyncQuickInfoSession session, out SnapshotSpan? span)
    {
        span = null;

        var snapshot = _textBuffer.CurrentSnapshot;
        var triggerPoint = session.GetTriggerPoint(_textBuffer).GetPoint(snapshot);

        var textBeforeCursor = new SnapshotSpan(snapshot, 0, triggerPoint.Position).GetText();

        var lastSemicolon = textBeforeCursor.LastIndexOf(';');
        var lastAt = textBeforeCursor.LastIndexOf('@');

        if (lastAt == -1 || lastAt < lastSemicolon)
        {
            return false;
        }

        var afterAt = textBeforeCursor.Substring(lastAt);
        var directive = afterAt.Split(' ')[0];

        if (directive != "@apply")
        {
            return false;
        }

        // Find where the class token under the cursor starts
        // afterAt is "@apply class1 class2..." — strip @apply and find the last word
        var lastSpace = afterAt.LastIndexOf(' ');
        var tokenStart = lastAt + lastSpace + 1;

        var x = textBeforeCursor[tokenStart];

        if (tokenStart >= snapshot.Length)
        {
            return false;
        }

        // Expand forward to find the end of the token
        int tokenEnd = triggerPoint.Position;
        while (tokenEnd < snapshot.Length)
        {
            char c = snapshot[tokenEnd];
            if (char.IsWhiteSpace(c) || c == ';' || c == '}')
            {
                break;
            }

            tokenEnd++;
        }

        if (tokenEnd <= tokenStart)
        {
            return false;
        }

        span = new SnapshotSpan(snapshot, tokenStart, tokenEnd - tokenStart);
        return true;
    }
}
