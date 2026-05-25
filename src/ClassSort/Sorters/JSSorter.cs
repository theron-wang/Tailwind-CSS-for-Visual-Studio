using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace TailwindCSSIntellisense.ClassSort.Sorters;

[Export(typeof(Sorter))]
internal class JSSorter : Sorter
{
    public override string[] Handled { get; } = [".jsx", ".tsx"];

    protected override async IAsyncEnumerable<string> GetSegmentsAsync(string filePath, string content)
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

            yield return await SortSegmentAsync(classContent, filePath);
            yield return total.Substring(total.IndexOf(classContent) + classContent.Length);
        }

        yield return content.Substring(lastIndex);
    }
}
