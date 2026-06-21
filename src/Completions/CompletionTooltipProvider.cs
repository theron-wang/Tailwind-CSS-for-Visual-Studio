using System.ComponentModel.Composition;
using System.Windows;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Utilities;

namespace TailwindCSSIntellisense.Completions;

[Export(typeof(IUIElementProvider<Completion, ICompletionSession>))]
[Name(nameof(CompletionTooltipCustomizationProvider))]
[Order(Before = "RoslynToolTipProvider")]
[ContentType("css")]
[ContentType("tcss")]
[ContentType("html")]
[ContentType("WebForms")]
[ContentType("razor")]
[ContentType("LegacyRazorCSharp")]
[ContentType("LegacyRazor")]
[ContentType("LegacyRazorCoreCSharp")]
internal class CompletionTooltipCustomizationProvider
    : IUIElementProvider<Completion, ICompletionSession>
{
    [Import]
    private DescriptionGenerator DescriptionGenerator { get; set; } = null!;

    public UIElement? GetUIElement(
        Completion itemToRender,
        ICompletionSession context,
        UIElementType elementType
    )
    {
        if (
            elementType == UIElementType.Tooltip
            && itemToRender.Properties.ContainsProperty("tailwind")
            && !itemToRender.Properties.ContainsProperty("variant")
        )
        {
            var fullText = itemToRender.DisplayText;

            if (fullText.EndsWith("[]"))
            {
                return null;
            }

            var project = itemToRender.Properties.GetProperty<ProjectCompletionValues>("tailwind");

            var isImportant = ImportantModifierHelper.IsImportantModifier(itemToRender.DisplayText);

            // Description property contains the class text parameter for GetDescription
            var desc = DescriptionGenerator.GetDescription(
                itemToRender.Description,
                project,
                ignorePrefix: true
            );

            if (desc is null)
            {
                return null;
            }

            if (desc is null)
            {
                return null;
            }

            return DescriptionUIHelper.GetDescriptionAsWPFFormatted(fullText, desc, isImportant);
        }
        else
        {
            return null;
        }
    }
}
