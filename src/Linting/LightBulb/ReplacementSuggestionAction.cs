using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace TailwindCSSIntellisense.Linting.LightBulb;

internal class ReplacementSuggestionAction(
    ITrackingSpan span,
    IEnumerable<SuggestionFix> fixes,
    string message
) : ISuggestedAction
{
    private readonly ITrackingSpan _span = span;
    private readonly string _message = message;
    private readonly IEnumerable<SuggestionFix> _fixes = fixes;

    public string DisplayText
    {
        get { return _message; }
    }

    public string? IconAutomationText
    {
        get { return null; }
    }

    ImageMoniker ISuggestedAction.IconMoniker
    {
        get { return default; }
    }

    public string? InputGestureText
    {
        get { return null; }
    }

    public bool HasActionSets
    {
        get { return false; }
    }

    public Task<IEnumerable<SuggestedActionSet>?> GetActionSetsAsync(
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<IEnumerable<SuggestedActionSet>?>([]);
    }

    public bool HasPreview
    {
        get { return true; }
    }

    public Task<object?> GetPreviewAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>(null);
    }

    public void Dispose() { }

    public void Invoke(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var snapshot = _span.TextBuffer.CurrentSnapshot;
        using var edit = _span.TextBuffer.CreateEdit();

        foreach (var fix in _fixes)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                edit.Cancel();
                return;
            }

            edit.Replace(fix.ApplicableTo.GetSpan(snapshot), fix.Replacement);
        }

        edit.Apply();
    }

    public bool TryGetTelemetryId(out Guid telemetryId)
    {
        telemetryId = Guid.Empty;
        return false;
    }
}
