using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

internal abstract class DiagnosticsChecker(bool isCssOnly, ErrorType errorType)
{
    [Import]
    protected readonly LinterUtilities _linterUtilities = null!;

    public bool IsCssOnly { get; } = isCssOnly;

    public ErrorType ErrorType { get; } = errorType;

    public IEnumerable<Error> GetErrors(
        SnapshotSpan span,
        ProjectCompletionValues projectCompletionValues,
        Func<string, IEnumerable<Match>> findClasses,
        Func<string, IEnumerable<Match>> splitClasses,
        Func<SnapshotSpan, bool> shouldNotAddErrors
    )
    {
        if (_linterUtilities.GetErrorSeverity(ErrorType) == ErrorSeverity.None)
        {
            return [];
        }

        return GetErrorsImpl(
            span,
            projectCompletionValues,
            findClasses,
            splitClasses,
            shouldNotAddErrors
        );
    }

    protected abstract IEnumerable<Error> GetErrorsImpl(
        SnapshotSpan span,
        ProjectCompletionValues projectCompletionValues,
        Func<string, IEnumerable<Match>> findClasses,
        Func<string, IEnumerable<Match>> splitClasses,
        Func<SnapshotSpan, bool> shouldNotAddErrors
    );

    /// <summary>
    /// Gets the full scope of the class, including the content within
    /// <br />
    /// Scope = class="class1 class2" OR @apply class1 class2
    /// <br />
    /// Content = class1 class2
    /// </summary>
    /// <remarks>
    /// Use instead of <seealso cref="Parsers.HtmlParser.GetClassAttributeValue(SnapshotPoint)"/> since this also gives the class= and quotation marks
    /// </remarks>
    protected (SnapshotPoint startOfScope, string? scope, string? content) GetFullScope(
        SnapshotSpan span,
        Func<string, IEnumerable<Match>> findClasses
    )
    {
        // If it already exists, do not expand scope
        var text = span.GetText();

        foreach (var match in findClasses(text))
        {
            var group = ClassRegexHelper.GetClassTextGroup(match);
            return (span.Start, match.Value, group.Value);
        }

        var start = Math.Max(0, (int)span.Start - 2000);

        var newSpan = new SnapshotSpan(
            span.Snapshot,
            start,
            Math.Min(span.Snapshot.Length, (int)span.End + 2000) - start
        );

        text = newSpan.GetText();

        foreach (var match in findClasses(text))
        {
            if (
                span.Start.Position >= newSpan.Start.Position + match.Index
                && span.Start.Position <= newSpan.Start.Position + match.Index + match.Length
            )
            {
                var group = ClassRegexHelper.GetClassTextGroup(match);
                return (newSpan.Start + match.Index, match.Value, group.Value);
            }
        }

        return (newSpan.Start, null, null);
    }

    /// <summary>
    /// Given a class content and a list of classes, gets the positions of each class within the content
    /// </summary>
    /// <param name="classContentStart">The starting position of the class content (* in class="* ...")</param>
    /// <param name="classContent">The class content; i.e., the stuff between the quotation marks</param>
    /// <param name="classList">The full class list</param>
    /// <returns>A dictionary mapping each class name to a list of its positions within the content</returns>
    protected Dictionary<string, List<SnapshotSpan>> GetClassPositions(
        SnapshotPoint classContentStart,
        string classContent,
        IEnumerable<string> classList
    )
    {
        Dictionary<string, List<SnapshotSpan>> classesToLocations = [];

        foreach (var className in classList.Distinct())
        {
            var index = 0;

            while (index < classContent!.Length)
            {
                index = classContent.IndexOf(className, index);

                if (index == -1)
                {
                    break;
                }

                var afterIndex = index + className.Length;
                var validStart = index == 0 || char.IsWhiteSpace(classContent[index - 1]);
                var validEnd =
                    afterIndex == classContent.Length
                    || char.IsWhiteSpace(classContent[afterIndex]);

                if (validStart && validEnd)
                {
                    var classSpan = new SnapshotSpan(
                        classContentStart.Snapshot,
                        classContentStart + index,
                        className.Length
                    );

                    if (classesToLocations.TryGetValue(className, out var locations))
                    {
                        locations.Add(classSpan);
                    }
                    else
                    {
                        classesToLocations[className] = [classSpan];
                    }
                }

                index += className.Length;
            }
        }

        return classesToLocations;
    }
}
