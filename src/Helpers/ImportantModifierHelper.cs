namespace TailwindCSSIntellisense.Helpers;

internal class ImportantModifierHelper
{
    public static bool IsImportantModifier(string classText)
    {
        return (classText.StartsWith("!") && !(classText.Length >= 2 && classText[1] == '!'))
            || (
                classText.EndsWith("!")
                && !(classText.Length >= 2 && classText[classText.Length - 2] == '!')
            );
    }
}
