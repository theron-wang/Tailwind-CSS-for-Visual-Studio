using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Linting.Validators;

namespace TailwindCSSIntellisense.Linting.ErrorList;

[Export(typeof(ITextViewCreationListener))]
[ContentType("razor")]
[ContentType("LegacyRazorCSharp")]
[ContentType("LegacyRazor")]
[ContentType("LegacyRazorCoreCSharp")]
[TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
internal class RazorErrorListListener : ErrorListListener
{
    protected override Validator GetValidator(ITextView view)
    {
        return RazorValidator.Create(
            view.TextBuffer,
            _linterUtilities,
            _projectConfigurationManager,
            _completionConfiguration,
            _diagnosticsAggregator
        );
    }
}
