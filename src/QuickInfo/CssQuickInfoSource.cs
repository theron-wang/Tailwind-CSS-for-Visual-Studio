using System.Linq;
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
        var startPos = new SnapshotPoint(_textBuffer.CurrentSnapshot, 0);
        var searchPos = session.GetTriggerPoint(_textBuffer).GetPoint(_textBuffer.CurrentSnapshot);

        var searchSnapshot = new SnapshotSpan(startPos, searchPos);
        var text = searchSnapshot.GetText();

        var lastIndexOfSemicolon = text.LastIndexOf(";");
        var lastIndexOfAt = text.LastIndexOf('@');

        if (lastIndexOfAt != -1 && lastIndexOfAt > lastIndexOfSemicolon)
        {
            var directive = text.Substring(lastIndexOfAt).Split(' ')[0];

            if (directive == "@apply" && text.EndsWith("@apply") == false)
            {
                text = text.Substring(lastIndexOfAt).Replace("@apply", "").Trim();

                var startIndex = lastIndexOfAt + "@apply".Length + 1;
                startIndex += text.LastIndexOf(' ') == -1 ? 0 : text.LastIndexOf(' ') + 1;
                var length = 1;

                searchSnapshot = new SnapshotSpan(_textBuffer.CurrentSnapshot, startIndex, length);
                var last = searchSnapshot.GetText().Last();

                while (char.IsWhiteSpace(last) == false && last != ';' && last != '}')
                {
                    length++;
                    searchSnapshot = new SnapshotSpan(
                        _textBuffer.CurrentSnapshot,
                        startIndex,
                        length
                    );
                    last = searchSnapshot.GetText().Last();
                }

                searchSnapshot = new SnapshotSpan(
                    _textBuffer.CurrentSnapshot,
                    startIndex,
                    length - 1
                );

                span = searchSnapshot;

                return true;
            }
            else
            {
                span = null;
                return false;
            }
        }
        else
        {
            span = null;
            return false;
        }
    }
}
