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
[ContentType("JavaScript")]
[ContentType("TypeScript")]
[ContentType("jsx")]
[TextViewRole(PredefinedTextViewRoles.Document)]
[TextViewRole(PredefinedTextViewRoles.Analyzable)]
internal class JSErrorTaggerProvider : ITaggerProvider
{
    [Import]
    public LinterUtilities LinterUtilities { get; set; } = null!;
    [Import]
    public ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;
    [Import]
    public CompletionConfiguration CompletionConfiguration { get; set; } = null!;

    public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {
        return (ITagger<T>)(ErrorTaggerBase)buffer.Properties.GetOrCreateSingletonProperty(() => new JSErrorTagger(buffer, LinterUtilities, ProjectConfigurationManager, CompletionConfiguration));
    }

    internal sealed class JSErrorTagger : ErrorTaggerBase, IDisposable
    {
        public JSErrorTagger(ITextBuffer buffer, LinterUtilities linterUtils, ProjectConfigurationManager completionUtilities, CompletionConfiguration completionConfiguration) : base(buffer, linterUtils)
        {
            _errorChecker = JSValidator.Create(buffer, linterUtils, completionUtilities, completionConfiguration);
            _errorChecker.Validated += UpdateErrors;
        }

        public void Dispose()
        {
            _errorChecker.Validated -= UpdateErrors;
        }
    }
}