using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Linting.Validators;
using TailwindCSSIntellisense.Linting.Validators.Diagnostics;

namespace TailwindCSSIntellisense.Linting.LightBulb;

[Export(typeof(ISuggestedActionsSourceProvider))]
[ContentType("JavaScript")]
[ContentType("TypeScript")]
[ContentType("jsx")]
[TextViewRole(PredefinedTextViewRoles.Document)]
[TextViewRole(PredefinedTextViewRoles.Analyzable)]
[Name(nameof(JSLightBulbSourceProvider))]
internal class JSLightBulbSourceProvider : ISuggestedActionsSourceProvider
{
    [Import]
    public readonly LinterUtilities _linterUtilities = null!;

    [Import]
    public readonly ProjectConfigurationManager _projectConfigurationManager = null!;

    [Import]
    private readonly CompletionConfiguration _completionConfiguration = null!;

    [Import]
    private readonly DiagnosticsAggregator _diagnosticsAggregator = null!;

    public ISuggestedActionsSource CreateSuggestedActionsSource(
        ITextView textView,
        ITextBuffer buffer
    )
    {
        var validator = buffer.Properties.GetOrCreateSingletonProperty(() =>
            JSValidator.Create(
                buffer,
                _linterUtilities,
                _projectConfigurationManager,
                _completionConfiguration,
                _diagnosticsAggregator
            )
        );

        return buffer.Properties.GetOrCreateSingletonProperty(() => new LightBulbSource(validator));
    }
}
