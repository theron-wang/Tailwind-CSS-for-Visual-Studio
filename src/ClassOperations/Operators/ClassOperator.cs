using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TailwindCSSIntellisense.ClassOperations.Operators;

/// <summary>
/// This abstract class defines the interface for a class operator, which is responsible for operating on classes in a file. It provides methods for running operations on files, formatting and operating on classes, and getting segments of a file. Derived classes must implement the abstract methods to define specific operations on classes.
/// </summary>
/// <remarks>
/// The contract of this class/implementation is as follows, using class sorting as an example:
/// <list type="bullet">
///     <item>ClassOperator&lt;T&gt;represents a class operator on a specific type of file that gives the class op tokens.</item>
///     <item>IClassOperation represents an interface for a class operation, such as sorting. A concrete class, like Sorter, should be passed in as T.</item>
///     <item>For each operation, create sealed concrete classes that define no members, but inherit XClassOperator&lt;T&gt;.</item>
/// </list>
/// </remarks>
internal abstract class ClassOperator<TOperation>
    where TOperation : IClassOperation, new()
{
    [Import]
    private TOperation _operation = default!;

    public abstract string[] Handled { get; }

    /// <summary>
    /// Runs the given operation on the file at the given path with the provided input, returning the result as a string.
    /// </summary>
    /// <param name="filePath">The file to run the operation on</param>
    /// <param name="input">The input to use for the operation</param>
    /// <returns>The result of the operation as a string (new file content)</returns>
    public async Task<string> RunOperationAsync(string filePath, string input)
    {
        var output = new StringBuilder();

        await foreach (var segment in GetSegmentsAsync(filePath, input))
        {
            if (segment.Type == ClassOpTokenType.Class)
            {
                var origClasses = segment.GetClasses().ToArray();

                var operatedClasses = await OperateOnClassesAsync(origClasses, filePath);
                output.Append(FormatClasses(operatedClasses, origClasses, segment.Raw));
            }
            else
            {
                output.Append(segment.Raw);
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Gets the segments of the file at the given path with the provided input, returning them as an asynchronous enumerable of ClassSegment objects.
    /// ClassSegment objects represent a segment of the file, which can be either a class segment or a non-class segment.
    /// </summary>
    /// <param name="filePath">The file path</param>
    /// <param name="input">The input text</param>
    /// <returns>The segments of the file</returns>
    protected abstract IAsyncEnumerable<ClassOpToken> GetSegmentsAsync(
        string filePath,
        string input
    );

    /// <summary>
    /// Formats the given IEnumerable of classes to match the newlines and indentation of the given classes and
    /// corresponding raw text. The raw text is masked by oldClasses, which should be in the same order as they appear in the raw text.
    /// This is useful for formatting classes which may have some newline in between (such as Razor classes).
    /// </summary>
    /// <param name="newClasses">The new classes to format</param>
    /// <param name="oldClasses">The old classes to use for the mask</param>
    /// <param name="rawText">The raw text that the classes were extracted from</param>
    /// <returns>The new classes, formatted with the original newlines and whitespace</returns>
    private string FormatClasses(
        IEnumerable<string> newClasses,
        IEnumerable<string> oldClasses,
        string rawText
    )
    {
        var masked = rawText.ToCharArray();
        var searchFrom = 0;

        foreach (var cls in oldClasses)
        {
            var start = rawText.IndexOf(cls, searchFrom, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            var end = start + cls.Length;
            for (var j = start; j < end; j++)
            {
                masked[j] = '-';
            }

            searchFrom = end;
        }

        var maskedText = new string(masked);

        var newlines = maskedText
            .Select((c, i) => (c, i))
            .Where(p => p.c == '\n')
            .Select(p => p.i)
            .ToList();

        var indents = newlines
            .Select(i =>
            {
                var start = i + 1;
                var end = start;
                while (end < maskedText.Length && char.IsWhiteSpace(maskedText[end]))
                {
                    end++;
                }
                return maskedText.Substring(start, end - start);
            })
            .ToList();

        var operatedSegment = new StringBuilder();

        int index = 0;
        int nextNewLineIndex = 0;

        var shouldMoveImportant = false;
        foreach (var operatedClass in newClasses)
        {
            if (this is CssClassOperator<TOperation> && operatedClass == "!important")
            {
                shouldMoveImportant = true;
                continue;
            }

            if (
                nextNewLineIndex < newlines.Count
                && index <= newlines[nextNewLineIndex]
                && newlines[nextNewLineIndex] <= index + operatedClass.Length
            )
            {
                operatedSegment
                    .AppendLine()
                    .Append(indents[nextNewLineIndex])
                    .Append(operatedClass);
                index += indents[nextNewLineIndex].Length;

                nextNewLineIndex++;
            }
            else
            {
                operatedSegment.Append(' ').Append(operatedClass);
            }

            index += operatedClass.Length + 1;
        }

        if (shouldMoveImportant)
        {
            operatedSegment.Append(" !important");
        }

        return operatedSegment.ToString().Trim();
    }

    /// <summary>
    /// Given a list of classes, this method will operate on them and return the result. The operation is defined in the derived class.
    /// </summary>
    /// <param name="classes">The input classes</param>
    /// <param name="filePath">The file path from which this class list originates</param>
    /// <returns>The result of the operation</returns>
    protected virtual Task<IEnumerable<string>> OperateOnClassesAsync(
        IEnumerable<string> classes,
        string filePath
    )
    {
        return _operation.OperateAsync(classes, filePath);
    }
}
