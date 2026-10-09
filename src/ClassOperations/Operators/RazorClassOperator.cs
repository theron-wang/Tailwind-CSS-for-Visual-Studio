using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TailwindCSSIntellisense.ClassOperations.Operators;

/// <summary>
/// An abstract class operator for Razor files (.razor and .cshtml) that handles class operations.
/// </summary>
/// <typeparam name="TOperation">The type of the class operation to be performed.</typeparam>
internal abstract class RazorClassOperator<TOperation> : ClassOperator<TOperation>
    where TOperation : IClassOperation, new()
{
    public override string[] Handled { get; } = [".razor", ".cshtml"];

    protected override async IAsyncEnumerable<ClassOpToken> GetSegmentsAsync(
        string filePath,
        string content
    )
    {
        int lastIndex = 0;
        int indexOfClass;

        foreach (var match in ClassRegexHelper.GetClassesRazorEnumerator(content))
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

            List<string> classes =
            [
                .. ClassRegexHelper.SplitRazorClasses(classContent).Select(m => m.Value),
            ];

            yield return new ClassOpToken()
            {
                Value = await OperateOnClassesAsync(classes, filePath),
                Type = ClassOpTokenType.Class,
            };

            yield return total.Substring(total.IndexOf(classContent) + classContent.Length);
        }

        yield return content.Substring(lastIndex);
    }

    protected override async Task<IEnumerable<string>> OperateOnClassesAsync(
        IEnumerable<string> classesEnumerable,
        string file
    )
    {
        Dictionary<string, string> unescapedToEscaped = [];
        HashSet<int> razorIndices = [];

        var classes = classesEnumerable.ToList();

        for (int i = 0; i < classes.Count; i++)
        {
            var c = classes[i];
            unescapedToEscaped[c.Replace("@@", "@").Replace("@(\"@\")", "@")] = c;
            if (!c.StartsWith("@@") && !c.StartsWith("@(\"@\")") && c.StartsWith("@"))
            {
                razorIndices.Add(i);
            }
        }

        var postOp = (
            await OperateOnClassesAsync(
                classes
                    .Select((v, i) => (v: v.Replace("@@", "@").Replace("@(\"@\")", "@"), i))
                    .Where(v => !razorIndices.Contains(v.i))
                    .Select(v => v.v),
                file
            )
        )
            .Select(c =>
            {
                return unescapedToEscaped.TryGetValue(c, out var value) ? value : c;
            })
            .ToList();

        int lastIndex = -1;
        int start = 0;

        List<string> result = [];

        foreach (int index in razorIndices)
        {
            int between = index - lastIndex - 1;

            if (between < 0)
            {
                between = 0;
            }

            result.AddRange(postOp.Skip(start).Take(between));
            result.Add(classes[index]);

            lastIndex = index;
            start += between;
        }

        return result.Concat(postOp.Skip(start));
    }
}
