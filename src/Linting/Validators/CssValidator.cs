using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Linting.Validators.Diagnostics;
using TailwindCSSIntellisense.Parsers;

namespace TailwindCSSIntellisense.Linting.Validators;

internal class CssValidator : Validator
{
    protected CssValidator(
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

    protected override IEnumerable<Error> ComputeErrors(SnapshotSpan span)
    {
        if (_projectCompletionValues is null)
        {
            return [];
        }

        return _diagnosticsAggregator.GetErrors(
            span,
            true,
            _projectCompletionValues,
            ClassRegexHelper.GetClassesCss,
            ClassRegexHelper.SplitNonRazorClasses,
            IsAlreadyChecked
        );
    }

    protected override IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span)
    {
        return CssParser.GetScopes(span);
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
            new CssValidator(
                buffer,
                linterUtils,
                completionUtilities,
                completionConfiguration,
                diagnosticsAggregator
            )
        );
    }
}
