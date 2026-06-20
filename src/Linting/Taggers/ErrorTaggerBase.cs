using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using TailwindCSSIntellisense.Linting.Validators;

namespace TailwindCSSIntellisense.Linting.Taggers;

internal abstract class ErrorTaggerBase(ITextBuffer buffer, LinterUtilities linterUtils)
    : ITagger<IErrorTag>
{
    protected readonly ITextBuffer _buffer = buffer;
    private readonly LinterUtilities _linterUtils = linterUtils;
    protected Validator _errorChecker = null!;

    public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

    public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (!_linterUtils.LinterEnabled())
        {
            return [];
        }

        var tags = new List<ITagSpan<IErrorTag>>();

        if (!spans.Any())
        {
            return tags;
        }

        foreach (var span in spans)
        {
            tags.AddRange(GetErrors(span));
        }

        return tags;
    }

    protected void UpdateErrors(IEnumerable<Span>? spans)
    {
        if (TagsChanged is not null)
        {
            if (spans is null)
            {
                var span = new SnapshotSpan(
                    _buffer.CurrentSnapshot,
                    0,
                    _buffer.CurrentSnapshot.Length
                );
                TagsChanged(this, new(span));
            }
            else
            {
                foreach (var span in spans)
                {
                    TagsChanged(this, new(new SnapshotSpan(_buffer.CurrentSnapshot, span)));
                }
            }
        }
    }

    private IEnumerable<ITagSpan<IErrorTag>> GetErrors(SnapshotSpan span)
    {
        foreach (var error in _errorChecker.GetErrors(span))
        {
            var tagSpan = _linterUtils.CreateTagSpan(
                error.Span.GetSpan(_buffer.CurrentSnapshot),
                error.ErrorMessage,
                error.ErrorType
            );
            if (tagSpan is not null)
            {
                yield return tagSpan;
            }
        }
    }
}
