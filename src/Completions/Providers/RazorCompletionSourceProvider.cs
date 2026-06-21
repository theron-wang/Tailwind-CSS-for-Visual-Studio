using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Completions.Sources;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions.Providers;

/// <summary>
/// A factory for creating <see cref="HtmlCompletionSource"/>; exported via MEF.
/// </summary>
[Export(typeof(ICompletionSourceProvider))]
[ContentType("razor")]
[ContentType("LegacyRazorCSharp")]
[ContentType("LegacyRazor")]
[ContentType("LegacyRazorCoreCSharp")]
[Name("TailwindCSS Razor Token Completion")]
[Order(After = Priority.Default, Before = Priority.High)]
internal class RazorCompletionSourceProvider : ICompletionSourceProvider
{
    [Import]
    internal ProjectConfigurationManager CompletionUtils { get; set; } = null!;

    [Import]
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    [Import]
    internal IAsyncCompletionBroker AsyncCompletionBroker { get; set; } = null!;

    [Import]
    internal ICompletionBroker CompletionBroker { get; set; } = null!;

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
        return new RazorCompletionSource(
            textBuffer,
            CompletionUtils,
            ColorIconGenerator,
            DescriptionGenerator,
            SettingsProvider,
            AsyncCompletionBroker,
            CompletionBroker,
            CompletionConfiguration,
            ProjectConfigurationInitializer
        );
    }
}
