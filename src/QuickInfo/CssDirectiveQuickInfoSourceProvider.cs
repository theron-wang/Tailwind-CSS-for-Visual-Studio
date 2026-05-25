using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.QuickInfo;

[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("CSS Directive Async Quick Info Provider")]
[ContentType("css")]
internal sealed class CssDirectiveQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    [Import]
    public ITextStructureNavigatorSelectorService ITextStructureNavigatorSelectorService { get; set; } = null!;

    [Import]
    public DirectoryVersionFinder DirectoryVersionFinder { get; set; } = null!;

    [Import]
    public SettingsProvider SettingsProvider { get; set; } = null!;

    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
    {
        return textBuffer.Properties.GetOrCreateSingletonProperty(() => new CssDirectiveQuickInfoSource(textBuffer, DirectoryVersionFinder, SettingsProvider, ITextStructureNavigatorSelectorService));
    }
}
