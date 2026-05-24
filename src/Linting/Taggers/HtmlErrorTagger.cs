using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Linting.Validators;

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
    public LinterUtilities LinterUtilities { get; set; } = null!;
    [Import]
    public ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;
    [Import]
    public CompletionConfiguration CompletionConfiguration { get; set; } = null!;

    public ITagger<T>? CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {
        // Handle legacy Razor editor; this completion controller is prioritized but
        // we should only use the Razor completion controller in that case
        if (buffer.IsLegacyRazorEditor())
        {
            return null;
        }

        return buffer.Properties.GetOrCreateSingletonProperty(() => new HtmlErrorTagger(buffer, LinterUtilities, ProjectConfigurationManager, CompletionConfiguration)) as ITagger<T>;
    }

    internal sealed class HtmlErrorTagger : ErrorTaggerBase, IDisposable
    {
        public HtmlErrorTagger(ITextBuffer buffer, LinterUtilities linterUtils, ProjectConfigurationManager completionUtilities, CompletionConfiguration completionConfiguration) : base(buffer, linterUtils)
        {
            _errorChecker = HtmlValidator.Create(buffer, linterUtils, completionUtilities, completionConfiguration);
            _errorChecker.Validated += UpdateErrors;
        }

        public void Dispose()
        {
            _errorChecker.Validated -= UpdateErrors;
        }
    }
}