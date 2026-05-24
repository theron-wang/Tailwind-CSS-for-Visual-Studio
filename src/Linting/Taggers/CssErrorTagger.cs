using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Linting.Validators;

namespace TailwindCSSIntellisense.Linting.Taggers;

[Export(typeof(ITaggerProvider))]
[TagType(typeof(IErrorTag))]
[ContentType("css")]
[ContentType("tcss")]
[TextViewRole(PredefinedTextViewRoles.Document)]
[TextViewRole(PredefinedTextViewRoles.Analyzable)]
internal class CssErrorTaggerProvider : ITaggerProvider
{
    [Import]
    public LinterUtilities LinterUtilities { get; set; } = null!;
    [Import]
    public ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;
    [Import]
    public CompletionConfiguration CompletionConfiguration { get; set; } = null!;

    public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {
        return (ITagger<T>)(ErrorTaggerBase)buffer.Properties.GetOrCreateSingletonProperty(() => new CssErrorTagger(buffer, LinterUtilities, ProjectConfigurationManager, CompletionConfiguration));
    }

    internal sealed class CssErrorTagger : ErrorTaggerBase
    {
        public CssErrorTagger(ITextBuffer buffer, LinterUtilities linterUtils, ProjectConfigurationManager completionUtilities, CompletionConfiguration completionConfiguration) : base(buffer, linterUtils)
        {
            _errorChecker = CssValidator.Create(buffer, linterUtils, completionUtilities, completionConfiguration);
            _errorChecker.Validated += UpdateErrors;
        }

        public void Dispose()
        {
            _errorChecker.Validated -= UpdateErrors;
        }
    }
}