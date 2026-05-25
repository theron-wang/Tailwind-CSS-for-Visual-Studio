using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;

namespace TailwindCSSIntellisense.ClassSort.Sorters;

[Export(typeof(Sorter))]
internal class RazorSorter : Sorter
{
    public override string[] Handled { get; } = [".razor", ".cshtml"];

    protected override async IAsyncEnumerable<string> GetSegmentsAsync(string filePath, string content)
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

            var tokens = new List<string>();
            var razorIndices = new HashSet<int>();

            // A dictionary of numbers to strings that represent where a new line + indent should be inserted;
            // each number represents a text token in tokens
            var newLines = new Dictionary<int, string>();

            foreach ((var token, var index) in ClassRegexHelper.SplitRazorClasses(classContent).Select((m, i) => (m, i)))
            {
                tokens.Add(token.Value);
                if (!token.Value.StartsWith("@@") && !token.Value.StartsWith("@(\"@\")") && token.Value.StartsWith("@"))
                {
                    razorIndices.Add(index);
                }
                if ((token.Index + token.Length < classContent.Length && classContent[token.Index + token.Length] == '\n') ||
                    // first case handles \n, second case handles \r\n
                    token.Index + token.Length + 1 < classContent.Length && classContent[token.Index + token.Length + 1] == '\n')
                {
                    // This means that after this current token, there should be a newline
                    var indent = new string([.. classContent.Substring(classContent.IndexOf('\n', token.Index + token.Length) + 1).TakeWhile(char.IsWhiteSpace)]);

                    newLines[index] = indent;
                }
            }

            var sorted = SortRazorSegmentAsync(tokens, razorIndices, filePath);

            var text = new StringBuilder();

            int i = 0;
            await foreach (var token in sorted)
            {
                text.Append(token);
                if (newLines.ContainsKey(i))
                {
                    text.AppendLine().Append(newLines[i]);
                }
                else
                {
                    text.Append(' ');
                }
                i++;
            }

            yield return text.ToString().Trim();
            yield return total.Substring(total.IndexOf(classContent) + classContent.Length);
        }

        yield return content.Substring(lastIndex);
    }

    private async IAsyncEnumerable<string> SortRazorSegmentAsync(List<string> classes, HashSet<int> razorIndices, string file)
    {
        Dictionary<string, string> unescapedToEscaped = [];

        foreach (var c in classes)
        {
            unescapedToEscaped[c.Replace("@@", "@").Replace("@(\"@\")", "@")] = c;
        }

        var sorted = (await SortAsync(classes
                .Select((v, i) => (v: v.Replace("@@", "@").Replace("@(\"@\")", "@"), i))
                .Where(v => !razorIndices.Contains(v.i))
                .Select(v => v.v), file
            )).Select(c =>
            {
                return unescapedToEscaped.TryGetValue(c, out var value) ? value : c;
            }).ToList();

        int lastIndex = -1;
        int start = 0;

        foreach (int index in razorIndices)
        {
            int between = index - lastIndex - 1;

            if (between < 0)
            {
                between = 0;
            }

            foreach (var s in sorted.Skip(start).Take(between))
            {
                yield return s;
            }
            yield return classes[index];

            lastIndex = index;
            start += between;
        }

        foreach (var s in sorted.Skip(start))
        {
            yield return s;
        }
    }

    private class Token(string text, bool isInRazor)
    {
        public string Text { get; set; } = text;
        public bool IsInRazor { get; set; } = isInRazor;
    }
}
