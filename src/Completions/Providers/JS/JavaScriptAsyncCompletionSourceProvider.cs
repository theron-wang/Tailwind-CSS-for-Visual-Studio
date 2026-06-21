using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Completions.Sources.JS;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions.Providers.JS;

[Export(typeof(IAsyncCompletionSourceProvider))]
[Order(Before = "High")]
[Name("JavaScriptAsyncCompletionSourceProvider")]
[ContentType("JavaScript")]
[ContentType("TypeScript")]
[ContentType("jsx")]
internal class JavaScriptAsyncCompletionSourceProvider : IAsyncCompletionSourceProvider
{
    private readonly IDictionary<ITextView, IAsyncCompletionSource> _cache =
        new Dictionary<ITextView, IAsyncCompletionSource>();

    [Import]
    public ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;

    [Import]
    public SettingsProvider SettingsProvider { get; set; } = null!;

    [Import]
    public DescriptionGenerator DescriptionGenerator { get; set; } = null!;

    [Import]
    public ColorIconGenerator ColorIconGenerator { get; set; } = null!;

    [Import]
    public CompletionConfiguration CompletionConfiguration { get; set; } = null!;

    [Import]
    public ProjectConfigurationInitializer ProjectConfigurationInitializer { get; set; } = null!;

    public IAsyncCompletionSource GetOrCreate(ITextView textView)
    {
        if (_cache.TryGetValue(textView, out var itemSource))
        {
            return itemSource;
        }

        var source = new JavaScriptAsyncCompletionSource(
            textView.TextBuffer,
            ProjectConfigurationManager,
            ColorIconGenerator,
            DescriptionGenerator,
            SettingsProvider,
            CompletionConfiguration,
            ProjectConfigurationInitializer
        );
        textView.Closed += (o, e) => _cache.Remove(textView);
        _cache[textView] = source;
        return source;
    }
}
