using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Completions.Sources;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions.Providers;

/// <summary>
/// A factory for creating <see cref="CssCompletionSource"/>; exported via MEF.
/// </summary>
[Export(typeof(ICompletionSourceProvider))]
[ContentType("css")]
[ContentType("tcss")]
[Name("TailwindCSS CSS Token Completion")]
[Order(After = Priority.Default, Before = Priority.High)]
internal class CssCompletionSourceProvider : ICompletionSourceProvider
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
        return new CssCompletionSource(
            textBuffer,
            CompletionUtils,
            ColorIconGenerator,
            DescriptionGenerator,
            SettingsProvider,
            CompletionConfiguration,
            ProjectConfigurationInitializer
        );
    }
}
