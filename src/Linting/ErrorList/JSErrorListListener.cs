using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using TailwindCSSIntellisense.Linting.Validators;

namespace TailwindCSSIntellisense.Linting.ErrorList;

[Export(typeof(ITextViewCreationListener))]
[ContentType("JavaScript")]
[ContentType("TypeScript")]
[ContentType("jsx")]
[TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
internal class JSErrorListListener : ErrorListListener
{
    protected override Validator GetValidator(ITextView view)
    {
        return JSValidator.Create(view.TextBuffer, _linterUtilities, _projectConfigurationManager, _completionConfiguration);
    }
}