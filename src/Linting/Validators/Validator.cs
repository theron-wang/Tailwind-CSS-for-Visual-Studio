using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Linting.Validators.Diagnostics;
using TailwindCSSIntellisense.Options;

namespace TailwindCSSIntellisense.Linting.Validators;

internal abstract class Validator : IDisposable
{
    protected readonly ITextBuffer _buffer;
    protected readonly LinterUtilities _linterUtils;
    protected readonly ProjectConfigurationManager _projectConfigurationManager;
    protected readonly CompletionConfiguration _completionConfiguration;
    protected readonly DiagnosticsAggregator _diagnosticsAggregator;
    protected ProjectCompletionValues? _projectCompletionValues;

    protected readonly List<ITrackingSpan> _checkedSpans = [];

    private ITextSnapshot? _snapshot;

    private readonly object _updateLock = new();

    private List<Error> _errors = [];

    /// <summary>
    /// Sends an <see cref="IEnumerable{T}"/> of type <see cref="Span"/> of changed spans or null if the entire document was revalidated
    /// </summary>
    public Action<IEnumerable<Span>?>? Validated;

    public Action<ITextBuffer>? BufferValidated;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget is ok"
    )]
    public Validator(
        ITextBuffer buffer,
        LinterUtilities linterUtils,
        ProjectConfigurationManager projectConfigurationManager,
        CompletionConfiguration completionConfiguration,
        DiagnosticsAggregator diagnosticsAggregator
    )
    {
        _buffer = buffer;
        _linterUtils = linterUtils;
        _projectConfigurationManager = projectConfigurationManager;
        _completionConfiguration = completionConfiguration;
        _diagnosticsAggregator = diagnosticsAggregator;
        _buffer.ChangedHighPriority += OnBufferChange;
        Linter.Saved += LinterOptionsChanged;
        _completionConfiguration.ConfigurationUpdated += ConfigurationUpdatedAsync;

        ThreadHelper
            .JoinableTaskFactory.RunAsync(ConfigurationUpdatedAsync)
            .FileAndForget(
                nameof(TailwindCSSIntellisense) + "/ClassCompletionGenerator/Initialize"
            );
    }

    /// <summary>
    /// Gets all the generated errors. This may give an empty list if the file has not been
    /// validated yet.
    /// </summary>
    /// <returns>A read-only list of all generated errors</returns>
    public IReadOnlyList<Error> GetAllErrors()
    {
        return _errors;
    }

    /// <summary>
    /// Gets the errors for a given span. This may give an empty enumerable if the file has not been
    /// validated yet.
    /// </summary>
    /// <param name="span">The span for which to get errors</param>
    /// <returns>Errors for spans intersecting with the given span</returns>
    public IEnumerable<Error> GetErrors(SnapshotSpan span)
    {
        var snapshot = span.Snapshot;
        return _errors.Where(err => span.IntersectsWith(err.Span.GetSpan(snapshot)));
    }

    private void OnBufferChange(object sender, TextContentChangedEventArgs e)
    {
        if (_linterUtils.LinterEnabled())
        {
            _snapshot = e.After;
            ThreadHelper
                .JoinableTaskFactory.StartOnIdle(() =>
                {
                    NormalUpdate(e);
                })
                .FileAndForget(nameof(TailwindCSSIntellisense) + "/Validator/OnBufferChange");
        }
    }

    private void StartUpdate()
    {
        if (_linterUtils.LinterEnabled())
        {
            ThreadHelper
                .JoinableTaskFactory.StartOnIdle(ForceUpdate)
                .FileAndForget(nameof(TailwindCSSIntellisense) + "/Validator/StartUpdate");
        }
    }

    private void ForceUpdate()
    {
        _errors.Clear();
        _checkedSpans.Clear();

        var scopes = GetScopes(
            new SnapshotSpan(_buffer.CurrentSnapshot, 0, _buffer.CurrentSnapshot.Length)
        );
        foreach (var scope in scopes)
        {
            if (IsAlreadyChecked(scope))
            {
                continue;
            }

            _errors.AddRange(ComputeErrors(scope));

            InsertCheckedSpan(
                scope.Snapshot.CreateTrackingSpan(scope, SpanTrackingMode.EdgeExclusive),
                scope.Snapshot
            );
        }

        Validated?.Invoke(null);
        BufferValidated?.Invoke(_buffer);
    }

    private void NormalUpdate(TextContentChangedEventArgs e)
    {
        lock (_updateLock)
        {
            var resolvedErrors = _errors.ToDictionary(err => err, err => err.Span.GetSpan(e.After));
            var resolvedChecked = _checkedSpans.ToDictionary(s => s, s => s.GetSpan(e.After));

            var changedSpans = e.Changes.Select(c => c.OldSpan).ToList();

            bool Overlaps(SnapshotSpan s) =>
                changedSpans.Any(c => s.IntersectsWith(c) || (c.IsEmpty && s.Contains(c.Start)));

            _errors.RemoveAll(err => Overlaps(resolvedErrors[err]));
            _checkedSpans.RemoveAll(s => Overlaps(resolvedChecked[s]));

            if (_snapshot is not null && _snapshot != e.After)
            {
                return;
            }

            List<Span> update = [];
            foreach (var change in e.Changes)
            {
                foreach (var scope in GetScopes(new SnapshotSpan(e.After, change.NewSpan)))
                {
                    var text = scope.GetText();
                    // Second-pass invalidation for scopes outside the raw change spans

                    // Use OverlapsWith instead of IntersectsWith here because we may accidentally
                    // invalidate errors whose span may have one edge coinciding with the scope
                    _errors.RemoveAll(err =>
                        (
                            resolvedErrors.TryGetValue(err, out var value)
                                ? value
                                : err.Span.GetSpan(e.After)
                        ).IntersectsWith(scope)
                    );

                    _checkedSpans.RemoveAll(s => resolvedChecked[s].IntersectsWith(scope));

                    update.Add(scope.Span);
                    if (IsAlreadyChecked(scope))
                    {
                        continue;
                    }

                    _errors.AddRange(ComputeErrors(scope));

                    var trackingSpan = scope.Snapshot.CreateTrackingSpan(
                        scope,
                        SpanTrackingMode.EdgeExclusive
                    );

                    InsertCheckedSpan(trackingSpan, e.After);
                    resolvedChecked[trackingSpan] = trackingSpan.GetSpan(e.After);
                }
            }

            Validated?.Invoke(update);
            BufferValidated?.Invoke(_buffer);
        }
    }

    protected bool IsAlreadyChecked(SnapshotSpan scope)
    {
        int lo = 0,
            hi = _checkedSpans.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var midSpan = _checkedSpans[mid].GetSpan(scope.Snapshot);
            if (midSpan.End <= scope.Start)
            {
                lo = mid + 1;
            }
            else if (midSpan.Start >= scope.End)
            {
                hi = mid - 1;
            }
            else
            {
                return midSpan.Contains(scope);
            }
        }
        return false;
    }

    private void InsertCheckedSpan(ITrackingSpan span, ITextSnapshot snapshot)
    {
        int start = span.GetSpan(snapshot).Start;
        int lo = 0,
            hi = _checkedSpans.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_checkedSpans[mid].GetSpan(snapshot).Start <= start)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        _checkedSpans.Insert(lo, span);
    }

    public void Dispose()
    {
        _buffer.ChangedHighPriority -= OnBufferChange;
        Linter.Saved -= LinterOptionsChanged;
        _completionConfiguration.ConfigurationUpdated -= ConfigurationUpdatedAsync;
    }

    private void LinterOptionsChanged(Linter linter)
    {
        StartUpdate();
    }

    private async Task ConfigurationUpdatedAsync()
    {
        _projectCompletionValues =
            await _projectConfigurationManager.GetCompletionConfigurationByFilePathAsync(
                _buffer.GetFileNameSafe()
            );
        StartUpdate();
    }

    protected abstract IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span);

    protected abstract IEnumerable<Error> ComputeErrors(SnapshotSpan span);
}
