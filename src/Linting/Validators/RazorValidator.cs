using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Parsers;

namespace TailwindCSSIntellisense.Linting.Validators;

internal class RazorValidator : HtmlLikeValidator
{
    protected override Func<string, IEnumerable<Match>> ClassSplitter { get; set; } = ClassRegexHelper.SplitRazorClasses;
    protected override Func<string, string, IEnumerable<Match>> ClassMatchGetter { get; set; } = ClassRegexHelper.GetClassesRazor;

    protected RazorValidator(ITextBuffer buffer, LinterUtilities linterUtils, ProjectConfigurationManager completionUtilities, CompletionConfiguration completionConfiguration) : base(buffer, linterUtils, completionUtilities, completionConfiguration)
    {

    }

    public override IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span)
    {
        return RazorParser.GetScopes(span);
    }

    public static Validator Create(ITextBuffer buffer, LinterUtilities linterUtils, ProjectConfigurationManager completionUtilities, CompletionConfiguration completionConfiguration)
    {
        return buffer.Properties.GetOrCreateSingletonProperty<Validator>(() => new RazorValidator(buffer, linterUtils, completionUtilities, completionConfiguration));
    }
}
