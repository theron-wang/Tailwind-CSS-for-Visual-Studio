using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;

namespace TailwindCSSIntellisense.Linting.Validators;

internal abstract class HtmlLikeValidator(ITextBuffer buffer, LinterUtilities linterUtils, ProjectConfigurationManager completionUtilities, CompletionConfiguration completionConfiguration)
    : Validator(buffer, linterUtils, completionUtilities, completionConfiguration)
{
    protected abstract Func<string, IEnumerable<Match>> ClassSplitter { get; set; }
    protected abstract Func<string, string, IEnumerable<Match>> ClassMatchGetter { get; set; }

    public override IEnumerable<Error> GetErrors(SnapshotSpan span, bool force = false)
    {
        if (_projectCompletionValues is null || (_checkedSpans.Any(s => s.Equals(span)) && !force))
        {
            yield break;
        }

        #region Css conflict
        if (_linterUtils.GetErrorSeverity(ErrorType.CssConflict) != ErrorSeverity.None)
        {
            (var scope, var content) = GetFullScope(span);

            if (string.IsNullOrEmpty(scope) || string.IsNullOrWhiteSpace(content))
            {
                yield break;
            }

            List<string> classes = [.. ClassSplitter(content!).Select(c => c.Value)];

            var classesByVariants = classes.GroupBy(c =>
            {
                var unescaped = c.Replace("@@", "@").Replace("@(\"@\")", "@");

                var index = unescaped.LastIndexOf(':');

                if (index == -1)
                {
                    return "";
                }
                return string.Join(":", unescaped.Substring(0, index).Split(':').OrderBy(x => x));
            });

            foreach (var grouping in classesByVariants)
            {
                // Find duplicates, since we are parsing from left to right
                int index = -1;
                foreach ((var className, var errorMessage) in _linterUtils.CheckForClassDuplicates(grouping, _projectCompletionValues))
                {
                    index = scope!.IndexOf(className, index + 1);

                    if (index == -1)
                    {
                        index = scope.IndexOf(className, index + 1);
                    }

                    while (index != -1 && index + className.Length < scope.Length && !char.IsWhiteSpace(scope[index + className.Length]) && !"\"'".Contains(scope[index + className.Length]))
                    {
                        index = scope.IndexOf(className, index + 1);
                    }

                    if (index == -1)
                    {
                        continue;
                    }

                    var errorSpan = new SnapshotSpan(_buffer.CurrentSnapshot, span.Span.Start + index, className.Length);

                    if (_checkedSpans.Contains(errorSpan) == false)
                    {
                        _checkedSpans.Add(errorSpan);
                        yield return new Error(errorSpan, errorMessage, ErrorType.CssConflict);
                    }
                }
            }
            _checkedSpans.Add(span);
        }
        #endregion
    }

    /// <summary>
    /// Gets the full scope of the class, including the content within
    /// <br />
    /// Scope = class="class1 class2"
    /// <br />
    /// Content = class1 class2
    /// </summary>
    /// <remarks>
    /// Use instead of <seealso cref="Parsers.HtmlParser.GetClassAttributeValue(SnapshotPoint)"/> since this also gives the class= and quotation marks
    /// </remarks>
    private (string? scope, string? content) GetFullScope(SnapshotSpan span)
    {
        // If it already exists, do not expand scope
        var text = span.GetText();

        foreach (var match in ClassMatchGetter(text, text))
        {
            var group = ClassRegexHelper.GetClassTextGroup(match);
            return (match.Value, group.Value);
        }

        var start = Math.Max(0, (int)span.Start - 2000);

        var newSpan = new SnapshotSpan(span.Snapshot, start, Math.Min(span.Snapshot.Length, (int)span.End + 2000) - start);

        text = newSpan.GetText();

        foreach (var match in ClassMatchGetter(text, text))
        {
            if (span.Start.Position >= newSpan.Start.Position + match.Index && span.Start.Position <= newSpan.Start.Position + match.Index + match.Length)
            {
                var group = ClassRegexHelper.GetClassTextGroup(match);
                return (match.Value, group.Value);
            }
        }

        return (null, null);
    }
}
