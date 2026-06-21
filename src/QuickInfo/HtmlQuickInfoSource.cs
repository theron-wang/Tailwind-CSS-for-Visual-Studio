using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Parsers;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.QuickInfo;

internal class HtmlQuickInfoSource(
    ITextBuffer textBuffer,
    DescriptionGenerator descriptionGenerator,
    ProjectConfigurationManager completionUtilities,
    SettingsProvider settingsProvider
) : QuickInfoSource(textBuffer, descriptionGenerator, completionUtilities, settingsProvider)
{
    protected override bool IsInClassScope(IAsyncQuickInfoSession session, out SnapshotSpan? span)
    {
        var searchPos = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);

        if (searchPos == null)
        {
            span = null;
            return false;
        }

        span = HtmlParser.GetClassAttributeValue(searchPos.Value);
        return span.HasValue;
    }
}
