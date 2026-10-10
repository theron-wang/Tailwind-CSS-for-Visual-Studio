using System.Collections.Generic;

namespace TailwindCSSIntellisense.ClassOperations.Operators;

/// <summary>
/// An abstract class that provides functionality for handling class operations in JavaScript and TypeScript files.
/// </summary>
/// <typeparam name="TOperation">The type of the class operation to be performed.</typeparam>
internal abstract class JsClassOperator<TOperation> : ClassOperator<TOperation>
    where TOperation : IClassOperation, new()
{
    public override string[] Handled { get; } = [".jsx", ".tsx"];

    protected override async IAsyncEnumerable<ClassOpToken> GetSegmentsAsync(
        string filePath,
        string content
    )
    {
        int lastIndex = 0;
        int indexOfClass;

        foreach (var match in ClassRegexHelper.GetClassesJavaScriptEnumerator(content))
        {
            indexOfClass = match.Index;

            // If we've already crossed over this part, don't sort again
            if (indexOfClass < lastIndex)
            {
                continue;
            }

            // Verify that we are in an HTML tag
            var closeAngleBracket = content.LastIndexOf('>', indexOfClass);
            var openAngleBracket = content.LastIndexOf('<', indexOfClass);

            if (openAngleBracket == -1 || closeAngleBracket > openAngleBracket)
            {
                continue;
            }

            yield return content.Substring(lastIndex, indexOfClass - lastIndex);

            lastIndex = match.Index + match.Length;

            if (lastIndex >= content.Length)
            {
                yield return content.Substring(indexOfClass);
                yield break;
            }

            // returns the text up to the content capture group (such as class=" or class=')
            var total = match.Value;
            var classContent = ClassRegexHelper.GetClassTextGroup(match).Value;
            yield return total.Substring(0, total.IndexOf(classContent));

            yield return new ClassOpToken() { Raw = classContent, Type = ClassOpTokenType.Class };
            yield return total.Substring(total.IndexOf(classContent) + classContent.Length);
        }

        yield return content.Substring(lastIndex);
    }
}
