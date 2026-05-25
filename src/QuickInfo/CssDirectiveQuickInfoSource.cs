using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Operations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.QuickInfo;

internal class CssDirectiveQuickInfoSource : IAsyncQuickInfoSource
{
    protected ITextBuffer _textBuffer;
    private readonly DirectoryVersionFinder _directoryVersionFinder;
    private readonly SettingsProvider _settingsProvider;
    private readonly ITextStructureNavigator _textStructureNavigator;


    public CssDirectiveQuickInfoSource(ITextBuffer textBuffer, DirectoryVersionFinder directoryVersionFinder, SettingsProvider settingsProvider, ITextStructureNavigatorSelectorService textStructureNavigatorSelectorService)
    {
        _textBuffer = textBuffer;
        _directoryVersionFinder = directoryVersionFinder;
        _settingsProvider = settingsProvider;
        _textStructureNavigator = textStructureNavigatorSelectorService.GetTextStructureNavigator(_textBuffer);
    }

    public void Dispose()
    {
    }

    public async Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
    {
        if (session.Content is null || session.Content.Any() || session.State == QuickInfoSessionState.Visible || session.State == QuickInfoSessionState.Dismissed)
        {
            return null;
        }

        var triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);

        if (triggerPoint is null)
        {
            return null;
        }

        var extent = _textStructureNavigator.GetExtentOfWord(triggerPoint.Value);

        if (extent.IsSignificant)
        {
            var text = extent.Span.GetText();

            if (text == "@apply")
            {
            }
            else if (await _directoryVersionFinder.GetTailwindVersionAsync(_textBuffer.GetFileNameSafe(), await _settingsProvider.GetSettingsAsync()) == TailwindVersion.V3 && (text == "@tailwind" || text == "@config"))
            {
            }
            else if (text == "@theme" || text == "@source" || text == "@utility" || text == "@custom-variant" || text == "@config" || text == "@plugin" || text == "@variant" || text.StartsWith("@slot"))
            {
            }
            else
            {
                return null;
            }

            var element = new ContainerElement(
                ContainerElementStyle.Stacked,
                new ClassifiedTextElement(
                        new ClassifiedTextRun(
                            PredefinedClassificationTypeNames.Type,
                            $"{text} is a valid Tailwind directive. Please disregard the error.",
                            ClassifiedTextRunStyle.Bold
                )));

            var span = _textBuffer.CurrentSnapshot.CreateTrackingSpan(extent.Span, SpanTrackingMode.EdgeInclusive);

            return new QuickInfoItem(span, element);
        }

        return null;
    }
}
