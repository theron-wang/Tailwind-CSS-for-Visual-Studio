using System.Collections.Generic;

namespace TailwindCSSIntellisense.ClassOperations.Operators;

/// <summary>
/// Class operator for CSS files. This class is responsible for handling the @apply directive in CSS and Tailwind CSS files.
/// It identifies segments of the file that contain @apply directives and processes them accordingly. The class is generic and
/// can be used with any type that implements the IClassOperation interface.
/// </summary>
/// <typeparam name="TOperation">An <see cref="IClassOperation"/> implementation that defines the operation for this class.</typeparam>
internal abstract class CssClassOperator<TOperation> : ClassOperator<TOperation>
    where TOperation : IClassOperation, new()
{
    public override string[] Handled { get; } = [".css", ".tcss"];

    protected override async IAsyncEnumerable<ClassOpToken> GetSegmentsAsync(
        string filePath,
        string content
    )
    {
        int indexOfApply = content.IndexOf("@apply");

        int lastIndex = 0;

        while (indexOfApply != -1)
        {
            // Verify that we are in the right context
            // Check to see if it is whitespace between here and { or ;, whichever comes first
            var context = content.LastIndexOfAny([';', '{'], indexOfApply) + 1;

            if (
                context == -1
                || !string.IsNullOrWhiteSpace(content.Substring(context, indexOfApply - context))
            )
            {
                indexOfApply = content.IndexOf("@apply", indexOfApply + 1);
                continue;
            }

            yield return content.Substring(lastIndex, indexOfApply - lastIndex);

            lastIndex = content.IndexOfAny([';', '}'], indexOfApply);

            if (lastIndex == -1)
            {
                yield return content.Substring(indexOfApply);
                yield break;
            }

            // return @apply
            yield return "@apply ";
            yield return new ClassOpToken()
            {
                Raw = content.Substring(indexOfApply + 6, lastIndex - (indexOfApply + 6)),
                Type = ClassOpTokenType.Class,
            };
            indexOfApply = content.IndexOf("@apply", indexOfApply + 1);
        }

        yield return content.Substring(lastIndex);
    }
}
