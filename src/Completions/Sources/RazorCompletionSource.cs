using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Parsers;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions.Sources;
/// <summary>
/// Completion provider for all HTML content files to provide Intellisense support for TailwindCSS classes
/// </summary>
internal class RazorCompletionSource : ClassCompletionGenerator, ICompletionSource
{
    private readonly IAsyncCompletionBroker _asyncCompletionBroker;
    private readonly ICompletionBroker _completionBroker;

    public RazorCompletionSource(ITextBuffer textBuffer, ProjectConfigurationManager completionUtils, ColorIconGenerator colorIconGenerator, DescriptionGenerator descriptionGenerator, SettingsProvider settingsProvider, IAsyncCompletionBroker asyncCompletionBroker, ICompletionBroker completionBroker, CompletionConfiguration completionConfiguration, ProjectConfigurationInitializer projectConfigurationInitializer)
        : base(textBuffer, completionUtils, colorIconGenerator, descriptionGenerator, settingsProvider, completionConfiguration, projectConfigurationInitializer)
    {
        _asyncCompletionBroker = asyncCompletionBroker;
        _completionBroker = completionBroker;

        _asyncCompletionBroker.CompletionTriggered += OnAsyncCompletionSessionStarted;
        Initialize();
    }

    /// <summary>
    /// Overrides the original completion set to include TailwindCSS classes
    /// </summary>
    /// <param name="session">Provided by Visual Studio</param>
    /// <param name="completionSets">Provided by Visual Studio</param>
    void ICompletionSource.AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
    {
        if (_settings is null)
        {
            return;
        }

        _showAutocomplete ??= _settings.EnableTailwindCss;

        if (_showAutocomplete == false || _settings.ConfigurationFiles.Count == 0)
        {
            return;
        }

        if (RazorParser.IsCursorInClassScope(session.TextView, out var classSpan) == false || classSpan is null)
        {
            return;
        }

        var truncatedClassSpan = new SnapshotSpan(classSpan.Value.Start, session.TextView.Caret.Position.BufferPosition);
        string classAttributeValueUpToPosition = truncatedClassSpan.GetText();

        var position = session.TextView.Caret.Position.BufferPosition.Position;
        ITextSnapshot snapshot = _textBuffer.CurrentSnapshot;
        var triggerPoint = session.GetTriggerPoint(snapshot)!.Value;

        if (triggerPoint == null)
            return;

        var line = triggerPoint.GetContainingLine();
        SnapshotPoint start = triggerPoint;

        var applicableTo = GetApplicableTo(triggerPoint, snapshot);
        var currentClassTotal = classAttributeValueUpToPosition.Split(' ').Last();

        var completions = GetCompletions(applicableTo.GetText(snapshot), isRazor: true);

        if (completionSets.Count == 1)
        {
            var defaultCompletionSet = completionSets[0];

            var newCompletionList = defaultCompletionSet.Completions.Concat(completions);

            var overridenCompletionSet = new TailwindCssCompletionSet(
                defaultCompletionSet.Moniker,
                defaultCompletionSet.DisplayName,
                applicableTo,
                newCompletionList,
                defaultCompletionSet.CompletionBuilders);

            // Overrides the original completion set so there aren't two different completion tabs
            completionSets.Clear();
            completionSets.Add(overridenCompletionSet);
        }
        else
        {
            completionSets.Add(new TailwindCssCompletionSet(
                "All",
                "All",
                applicableTo,
                completions,
                new List<Completion>()));
        }
    }

    private void OnAsyncCompletionSessionStarted(object sender, CompletionTriggeredEventArgs e)
    {
        var sessions = _completionBroker.GetSessions(e.TextView);

        var tailwindSession = sessions.FirstOrDefault(s => s.IsStarted && !s.IsDismissed && s.SelectedCompletionSet is TailwindCssCompletionSet);

        if (tailwindSession != null && RazorParser.IsCursorInClassScope(tailwindSession.TextView, out var classSpan) && classSpan is not null && tailwindSession.SelectedCompletionSet is TailwindCssCompletionSet tailwindCompletionSet)
        {
            var otherSessions = sessions.Where(s => s != tailwindSession);
            tailwindCompletionSet.AddCompletions(e.CompletionSession.GetComputedItems(default)
                .Items
                .Where(c => c.DisplayText.StartsWith(classSpan.Value.GetText().Split(' ').Last(), StringComparison.InvariantCultureIgnoreCase))
                .Select(c => new Completion3(c.DisplayText, c.InsertText, null, c.Icon == null ? KnownMonikers.LocalVariable : new ImageMoniker() { Guid = c.Icon.ImageId.Guid, Id = c.Icon.ImageId.Id }, null)));

            e.CompletionSession.Dismiss();
            foreach (var session in otherSessions)
            {
                if (!session.IsDismissed)
                {
                    if (!session.IsStarted)
                    {
                        session.Start();
                    }
                    tailwindCompletionSet.AddCompletions(session.SelectedCompletionSet.Completions);
                    session.Dismiss();
                }
            }

            // Prevent word cutoff by re-rendering completion GUI
            tailwindSession.Filter();
        }
    }

    private ITrackingSpan GetApplicableTo(SnapshotPoint triggerPoint, ITextSnapshot snapshot)
    {
        var span = RazorParser.GetClassAttributeValue(triggerPoint);
        // span should not be null since this is called after we verify the cursor is in a class context
        return snapshot.CreateTrackingSpan(new SnapshotSpan(span!.Value.Start, triggerPoint), SpanTrackingMode.EdgeInclusive);
    }

    public override void Dispose()
    {
        _asyncCompletionBroker.CompletionTriggered -= OnAsyncCompletionSessionStarted;
        base.Dispose();
    }
}
