using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.QuickInfo;

[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("CSS Async Quick Info Provider")]
[ContentType("css")]
internal sealed class CssQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    [Import]
    private readonly DescriptionGenerator _descriptionGenerator = null!;

    [Import]
    private readonly ProjectConfigurationManager _projectConfigurationManager = null!;

    [Import]
    private readonly SettingsProvider _settingsProvider = null!;

    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
    {
        return textBuffer.Properties.GetOrCreateSingletonProperty(() =>
            new CssQuickInfoSource(
                textBuffer,
                _descriptionGenerator,
                _projectConfigurationManager,
                _settingsProvider
            )
        );
    }
}
