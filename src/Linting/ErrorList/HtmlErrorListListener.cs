using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using TailwindCSSIntellisense.Linting.Validators;

namespace TailwindCSSIntellisense.Linting.ErrorList;

[Export(typeof(ITextViewCreationListener))]
[ContentType("html")]
[ContentType("WebForms")]
[TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
internal class HtmlErrorListListener : ErrorListListener
{
    public override void TextViewCreated(ITextView view)
    {
        if (!LegacyRazorEditorHelper.IsLegacyRazorEditor(view.TextBuffer))
        {
            base.TextViewCreated(view);
        }
    }

    protected override Validator GetValidator(ITextView view)
    {
        return HtmlValidator.Create(view.TextBuffer, _linterUtilities, _projectConfigurationManager, _completionConfiguration);
    }
}