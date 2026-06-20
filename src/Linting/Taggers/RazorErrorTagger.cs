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
[ContentType("razor")]
[ContentType("LegacyRazorCSharp")]
[ContentType("LegacyRazor")]
[ContentType("LegacyRazorCoreCSharp")]
[TextViewRole(PredefinedTextViewRoles.Document)]
[TextViewRole(PredefinedTextViewRoles.Analyzable)]
internal class RazorErrorTaggerProvider : ITaggerProvider
{
    [Import]
    public readonly LinterUtilities _linterUtilities = null!;

    [Import]
    public readonly ProjectConfigurationManager _projectConfigurationManager = null!;

    [Import]
    private readonly CompletionConfiguration _completionConfiguration = null!;

    [Import]
    private readonly DiagnosticsAggregator _diagnosticsAggregator = null!;

    public ITagger<T> CreateTagger<T>(ITextBuffer buffer)
        where T : ITag
    {
        return (ITagger<T>)
            (ErrorTaggerBase)
                buffer.Properties.GetOrCreateSingletonProperty(() =>
                    new RazorErrorTagger(
                        buffer,
                        _linterUtilities,
                        _projectConfigurationManager,
                        _completionConfiguration,
                        _diagnosticsAggregator
                    )
                );
    }

    internal sealed class RazorErrorTagger : ErrorTaggerBase, IDisposable
    {
        public RazorErrorTagger(
            ITextBuffer buffer,
            LinterUtilities linterUtils,
            ProjectConfigurationManager completionUtilities,
            CompletionConfiguration completionConfiguration,
            DiagnosticsAggregator diagnosticsAggregator
        )
            : base(buffer, linterUtils)
        {
            _errorChecker = buffer.Properties.GetOrCreateSingletonProperty(() =>
                RazorValidator.Create(
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
