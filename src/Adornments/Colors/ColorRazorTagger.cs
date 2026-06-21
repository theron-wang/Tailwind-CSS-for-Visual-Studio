using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Parsers;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Adornments.Colors;

[Export(typeof(IViewTaggerProvider))]
[TagType(typeof(IntraTextAdornmentTag))]
[ContentType("razor")]
[ContentType("LegacyRazorCSharp")]
[ContentType("LegacyRazor")]
[ContentType("LegacyRazorCoreCSharp")]
[TextViewRole(PredefinedTextViewRoles.Document)]
[TextViewRole(PredefinedTextViewRoles.Analyzable)]
internal sealed class ColorRazorTaggerProvider : IViewTaggerProvider
{
    [Import]
    private readonly ProjectConfigurationManager _projectConfigurationManager = null!;

    [Import]
    private readonly CompletionConfiguration _completionConfiguration = null!;

    [Import]
    private readonly SettingsProvider _settingsProvider = null!;

    public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer)
        where T : ITag
    {
        return (ITagger<T>)
            buffer.Properties.GetOrCreateSingletonProperty(() =>
                new ColorRazorTagger(
                    buffer,
                    textView,
                    _projectConfigurationManager,
                    _completionConfiguration,
                    _settingsProvider
                )
            );
    }

    private class ColorRazorTagger(
        ITextBuffer buffer,
        ITextView view,
        ProjectConfigurationManager completionUtilities,
        CompletionConfiguration completionConfiguration,
        SettingsProvider settingsProvider
    )
        : ColorTaggerBase(
            buffer,
            view,
            completionUtilities,
            completionConfiguration,
            settingsProvider
        )
    {
        protected override IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span)
        {
            foreach (var classAttributeSpan in RazorParser.GetClassAttributeValues(span))
            {
                var text = classAttributeSpan.GetText();

                foreach (var split in ClassRegexHelper.SplitRazorClasses(text))
                {
                    yield return new SnapshotSpan(
                        span.Snapshot,
                        classAttributeSpan.Start + split.Index,
                        split.Value.Length
                    );
                }
            }
        }
    }
}
