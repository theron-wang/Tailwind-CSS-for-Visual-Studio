using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using TailwindCSSIntellisense.Options;

namespace TailwindCSSIntellisense.Completions;

/// <summary>
/// Represents a set of TailwindCSS completions
/// </summary>
internal class TailwindCssCompletionSet : CompletionSet
{
    private readonly BulkInsertObservableCollection<Completion> _completions = [];
    private FilteredObservableCollection<Completion> _filteredCompletions = null!;
    private int _filterBufferTextVersionNumber;
    private string? _filterBufferText;

    private string? FilterBufferText
    {
        get
        {
            if (ApplicableTo != null)
            {
                ITextSnapshot currentSnapshot = ApplicableTo.TextBuffer.CurrentSnapshot;
                if (_filterBufferText == null || _filterBufferTextVersionNumber != currentSnapshot.Version.VersionNumber)
                {
                    _filterBufferText = ApplicableTo.GetText(currentSnapshot);
                    _filterBufferTextVersionNumber = currentSnapshot.Version.VersionNumber;
                }
            }

            return _filterBufferText;
        }
    }

    /// <inheritdoc />
    public TailwindCssCompletionSet(string moniker, string displayName, ITrackingSpan applicableTo, IEnumerable<Completion> completions, IEnumerable<Completion> completionBuilders) : base(moniker, displayName, applicableTo, completions, completionBuilders)
    {
        _completions.AddRange(completions);
        Initialize();
    }

    /// <summary>
    /// Adds completions to the existing completion set (used to combine Tailwind completions with shim completions)
    /// </summary>
    /// <param name="completions">The list of completions to add</param>
    /// 

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously", Justification = "Not expensive")]
    public void AddCompletions(IEnumerable<Completion> completions)
    {
        var addToEnd = ThreadHelper.JoinableTaskFactory.Run(General.GetLiveInstanceAsync).TailwindCompletionsComeFirst;

        if (addToEnd)
        {
            _completions.AddRange(completions
                .Where(c => _completions.Any(c2 => c2.DisplayText == c.DisplayText) == false));
        }
        else
        {
            _completions.AddRangeToBeginning(completions
                .Where(c => _completions.Any(c2 => c2.DisplayText == c.DisplayText) == false));
        }
    }

    private void Initialize()
    {
        _filteredCompletions = new FilteredObservableCollection<Completion>(_completions);
    }

    /// <inheritdoc />
    public override IList<Completion> Completions => _filteredCompletions;

    /// <inheritdoc />
    public override void Filter()
    {
        if (string.IsNullOrEmpty(FilterBufferText))
        {
            _filteredCompletions.StopFiltering();
            return;
        }

        var filterText = FilterBufferText!.Replace("@@", "@").Replace("@(\"@\")", "@");

        _filteredCompletions.Filter(c =>
        {
            if (string.IsNullOrWhiteSpace(c.DisplayText))
            {
                return c.DisplayText.EndsWith(":");
            }
            else
            {
                var segments = c.DisplayText.Split([':'], StringSplitOptions.RemoveEmptyEntries).Last().Split('-');
                var filterSegments = filterText!.Split(':').Last().Split(['-'], StringSplitOptions.RemoveEmptyEntries);
                return filterSegments.Length == 0 || filterSegments.All(s => segments.Contains(s) || segments.Any(s2 => s2.StartsWith(s)));
            }
        });
    }

    /// <inheritdoc />
    public override void SelectBestMatch()
    {
        Completion? completionSelection = null;

        if (_filteredCompletions.Count == 1)
        {
            SelectionStatus = new CompletionSelectionStatus(_filteredCompletions[0], true, true);
        }
        else if (string.IsNullOrWhiteSpace(FilterBufferText) == false && string.IsNullOrWhiteSpace(FilterBufferText!.Split(':').Last()) == false)
        {
            foreach (var completion in Completions)
            {
                if (completion.InsertionText == FilterBufferText)
                {
                    SelectionStatus = new CompletionSelectionStatus(completion, true, true);
                    return;
                }
                else if (completion.InsertionText.StartsWith(FilterBufferText))
                {
                    if (completionSelection == null || completion.InsertionText.Length < completionSelection.InsertionText.Length)
                    {
                        completionSelection = completion;
                    }
                }
            }
        }

        if (completionSelection != null)
        {
            SelectionStatus = new CompletionSelectionStatus(completionSelection, false, true);
        }
        else
        {
            SelectBestMatch(CompletionMatchType.MatchInsertionText, false);
        }
    }
}
