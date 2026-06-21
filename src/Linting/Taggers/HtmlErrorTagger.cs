using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Linting.Validators;
using TailwindCSSIntellisense.Linting.Validators.Diagnostics;

namespace TailwindCSSIntellisense.Linting.Taggers;

[Export(typeof(ITaggerProvider))]
[TagType(typeof(IErrorTag))]
[ContentType("html")]
[ContentType("WebForms")]
[TextViewRole(PredefinedTextViewRoles.Document)]
[TextViewRole(PredefinedTextViewRoles.Analyzable)]
internal class HtmlErrorTaggerProvider : ITaggerProvider
{
    [Import]
    public readonly LinterUtilities _linterUtilities = null!;

    [Import]
    public readonly ProjectConfigurationManager _projectConfigurationManager = null!;

    [Import]
    private readonly CompletionConfiguration _completionConfiguration = null!;

    [Import]
    private readonly DiagnosticsAggregator _diagnosticsAggregator = null!;

    public ITagger<T>? CreateTagger<T>(ITextBuffer buffer)
        where T : ITag
    {
        // Handle legacy Razor editor; this completion controller is prioritized but
        // we should only use the Razor completion controller in that case
        if (buffer.IsLegacyRazorEditor())
        {
            return null;
        }

        return buffer.Properties.GetOrCreateSingletonProperty(() =>
                new HtmlErrorTagger(
                    buffer,
                    _linterUtilities,
                    _projectConfigurationManager,
                    _completionConfiguration,
                    _diagnosticsAggregator
                )
            ) as ITagger<T>;
    }

    internal sealed class HtmlErrorTagger : ErrorTaggerBase, IDisposable
    {
        public HtmlErrorTagger(
            ITextBuffer buffer,
            LinterUtilities linterUtils,
            ProjectConfigurationManager completionUtilities,
            CompletionConfiguration completionConfiguration,
            DiagnosticsAggregator diagnosticsAggregator
        )
            : base(buffer, linterUtils)
        {
            _errorChecker = buffer.Properties.GetOrCreateSingletonProperty(() =>
                HtmlValidator.Create(
                    buffer,
                    linterUtils,
                    completionUtilities,
                    completionConfiguration,
                    diagnosticsAggregator
                )
            );
            _errorChecker.Validated += UpdateErrors;
        }

        public void Dispose()
        {
            _errorChecker.Validated -= UpdateErrors;
        }
    }
}
