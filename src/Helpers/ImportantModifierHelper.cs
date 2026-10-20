namespace TailwindCSSIntellisense.Helpers;

internal class ImportantModifierHelper
{
    public static bool IsImportantModifier(string classText)
    {
        var parts = classText.SplitClassVariants();
        classText = parts[parts.Count - 1];
        return (classText.StartsWith("!") && !(classText.Length >= 2 && classText[1] == '!'))
            || (
                classText.EndsWith("!")
                && !(classText.Length >= 2 && classText[classText.Length - 2] == '!')
            );
    }
}
