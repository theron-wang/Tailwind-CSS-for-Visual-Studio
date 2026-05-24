using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using TailwindCSSIntellisense.Completions.Sources;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions.Providers;

/// <summary>
/// A factory for creating <see cref="HtmlCompletionSource"/>; exported via MEF.
/// </summary>
[Export(typeof(ICompletionSourceProvider))]
[ContentType("html")]
[ContentType("WebForms")]
[Name("TailwindCSS HTML Token Completion")]
[Order(After = Priority.Default, Before = Priority.High)]
internal class HtmlCompletionSourceProvider : ICompletionSourceProvider
{
    [Import]
    internal ProjectConfigurationManager CompletionUtils { get; set; } = null!;
    [Import]
    internal SettingsProvider SettingsProvider { get; set; } = null!;
    [Import]
    internal DescriptionGenerator DescriptionGenerator { get; set; } = null!;
    [Import]
    internal ColorIconGenerator ColorIconGenerator { get; set; } = null!;
    [Import]
    public CompletionConfiguration CompletionConfiguration { get; set; } = null!;
    [Import]
    public ProjectConfigurationInitializer ProjectConfigurationInitializer { get; set; } = null!;

    public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer)
    {
        return new HtmlCompletionSource(textBuffer, CompletionUtils, ColorIconGenerator, DescriptionGenerator, SettingsProvider, CompletionConfiguration, ProjectConfigurationInitializer);
    }
}
