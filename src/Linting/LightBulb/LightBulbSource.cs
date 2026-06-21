using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Linting.Validators;

namespace TailwindCSSIntellisense.Linting.LightBulb;

internal class LightBulbSource(Validator validator) : ISuggestedActionsSource
{
    private readonly Validator _validator = validator;

#pragma warning disable 0067
    public event EventHandler<EventArgs>? SuggestedActionsChanged;
#pragma warning restore 0067

    public void Dispose() { }

    public IEnumerable<SuggestedActionSet> GetSuggestedActions(
        ISuggestedActionCategorySet requestedActionCategories,
        SnapshotSpan range,
        CancellationToken cancellationToken
    )
    {
        foreach (var error in _validator.GetErrors(range))
        {
            if (error.Suggestion is null)
            {
                continue;
            }

            yield return new SuggestedActionSet(
                PredefinedSuggestedActionCategoryNames.CodeFix,
                [
                    new ReplacementSuggestionAction(
                        error.Span,
                        error.Suggestion.SuggestedFix,
                        error.Suggestion.Message
                    ),
                ],
                priority: SuggestedActionSetPriority.Medium
            );
        }
    }

    public Task<bool> HasSuggestedActionsAsync(
        ISuggestedActionCategorySet requestedActionCategories,
        SnapshotSpan range,
        CancellationToken cancellationToken
    )
    {
        foreach (var error in _validator.GetErrors(range))
        {
            if (error.Suggestion is not null)
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public bool TryGetTelemetryId(out Guid telemetryId)
    {
        telemetryId = Guid.Empty;
        return false;
    }
}
