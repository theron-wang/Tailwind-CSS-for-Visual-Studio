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
[ContentType("JavaScript")]
[ContentType("TypeScript")]
[ContentType("jsx")]
[TextViewRole(PredefinedTextViewRoles.Document)]
[TextViewRole(PredefinedTextViewRoles.Analyzable)]
internal sealed class ColorJSTaggerProvider : IViewTaggerProvider
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
                new ColorJSTagger(
                    buffer,
                    textView,
                    _projectConfigurationManager,
                    _completionConfiguration,
                    _settingsProvider
                )
            );
    }

    private class ColorJSTagger(
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
        protected override IEnumerable<SnapshotSpan> GetScopes(
            SnapshotSpan span,
            ITextSnapshot snapshot
        )
        {
            foreach (var classAttributeSpan in JSParser.GetClassAttributeValues(span))
            {
                var text = classAttributeSpan.GetText();

                foreach (var split in ClassRegexHelper.SplitNonRazorClasses(text))
                {
                    yield return new SnapshotSpan(
                        snapshot,
                        classAttributeSpan.Start + split.Index,
                        split.Value.Length
                    );
                }
            }
        }
    }
}
