using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using TailwindCSSIntellisense.Linting.Validators;

namespace TailwindCSSIntellisense.Linting.ErrorList;

[Export(typeof(ITextViewCreationListener))]
[ContentType("css")]
[TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
internal class CssErrorListListener : ErrorListListener
{
    protected override Validator GetValidator(ITextView view)
    {
        return CssValidator.Create(view.TextBuffer, _linterUtilities, _projectConfigurationManager, _completionConfiguration);
    }
}