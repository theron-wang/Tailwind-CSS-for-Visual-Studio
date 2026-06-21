using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.QuickInfo;

[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("HTML Async Quick Info Provider")]
[ContentType("html")]
[ContentType("WebForms")]
internal sealed class HtmlQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    [Import]
    private readonly DescriptionGenerator _descriptionGenerator = null!;

    [Import]
    private readonly ProjectConfigurationManager _projectConfigurationManager = null!;

    [Import]
    private readonly SettingsProvider _settingsProvider = null!;

    public IAsyncQuickInfoSource? TryCreateQuickInfoSource(ITextBuffer textBuffer)
    {
        // Handle legacy Razor editor; this completion controller is prioritized but
        // we should only use the Razor completion controller in that case
        if (textBuffer.IsLegacyRazorEditor())
        {
            return null;
        }
        return textBuffer.Properties.GetOrCreateSingletonProperty(() =>
            new HtmlQuickInfoSource(
                textBuffer,
                _descriptionGenerator,
                _projectConfigurationManager,
                _settingsProvider
            )
        );
    }
}
