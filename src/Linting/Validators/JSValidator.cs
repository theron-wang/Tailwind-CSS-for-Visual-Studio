using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Linting.Validators.Diagnostics;
using TailwindCSSIntellisense.Parsers;

namespace TailwindCSSIntellisense.Linting.Validators;

internal class JSValidator : Validator
{
    protected JSValidator(
        ITextBuffer buffer,
        LinterUtilities linterUtils,
        ProjectConfigurationManager completionUtilities,
        CompletionConfiguration completionConfiguration,
        DiagnosticsAggregator diagnosticsAggregator
    )
        : base(
            buffer,
            linterUtils,
            completionUtilities,
            completionConfiguration,
            diagnosticsAggregator
        ) { }

    protected override async IAsyncEnumerable<Error> ComputeErrorsAsync(SnapshotSpan span)
    {
        if (_projectCompletionValues is null)
        {
            yield break;
        }

        await foreach (
            var error in _diagnosticsAggregator.GetErrorsAsync(
                span,
                false,
                _projectCompletionValues,
                ClassRegexHelper.GetClassesJavaScript,
                ClassRegexHelper.SplitNonRazorClasses,
                IsAlreadyChecked
            )
        )
        {
            yield return error;
        }
    }

    protected override IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span)
    {
        return JSParser.GetScopes(span);
    }

    public static Validator Create(
        ITextBuffer buffer,
        LinterUtilities linterUtils,
        ProjectConfigurationManager completionUtilities,
        CompletionConfiguration completionConfiguration,
        DiagnosticsAggregator diagnosticsAggregator
    )
    {
        return buffer.Properties.GetOrCreateSingletonProperty<Validator>(() =>
            new JSValidator(
                buffer,
                linterUtils,
                completionUtilities,
                completionConfiguration,
                diagnosticsAggregator
            )
        );
    }
}
