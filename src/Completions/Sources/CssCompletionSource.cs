using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions.Sources;

/// <summary>
/// Completion provider for all CSS files to provide Intellisense support for TailwindCSS classes, functions, and directives
/// </summary>
internal class CssCompletionSource : ClassCompletionGenerator, ICompletionSource
{
    private UnsetProjectCompletionValues? _unsetProjectCompletionValues;

    public CssCompletionSource(ITextBuffer textBuffer, ProjectConfigurationManager completionUtils, ColorIconGenerator colorIconGenerator, DescriptionGenerator descriptionGenerator, SettingsProvider settingsProvider, CompletionConfiguration completionConfiguration, ProjectConfigurationInitializer projectCompletionInit) : base(textBuffer, completionUtils, colorIconGenerator, descriptionGenerator, settingsProvider, completionConfiguration, projectCompletionInit)
    {
        Initialize();
    }

    /// <summary>
    /// Provides relevant TailwindCSS completions
    /// </summary>
    /// <param name="session">VS provided</param>
    /// <param name="completionSets">VS provided</param>
    void ICompletionSource.AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
    {
        if (_settings is null)
        {
            return;
        }

        _showAutocomplete ??= _settings.EnableTailwindCss;

        if (_showAutocomplete == false || _settings.ConfigurationFiles.Count == 0 || _projectCompletionValues is null || _unsetProjectCompletionValues is null)
        {
            return;
        }

        var completions = new List<Completion>();

        var position = session.TextView.Caret.Position.BufferPosition.Position;
        var snapshot = _textBuffer.CurrentSnapshot;
        var triggerPoint = session.GetTriggerPoint(snapshot);

        if (triggerPoint == null)
        {
            return;
        }

        var applicableTo = GetApplicableTo(triggerPoint.Value, snapshot);

        var line = triggerPoint.Value.GetContainingLine();

        if (!IsCaretInBlock(session, out bool isInBaseDirectiveBlock, "@media", "@layer", "@theme"))
        {
            if (IsUsingDirective(session, out var directive))
            {
                if (_projectCompletionValues.Version == TailwindVersion.V3)
                {
                    switch (directive)
                    {
                        case "@tailwind":
                            completions =
                            [
                                new("base", "base", "base", ProjectConfigurationManager.TailwindLogo, null),
                                new("components", "components", "components", ProjectConfigurationManager.TailwindLogo, null),
                                new("utilities", "utilities", "utilities", ProjectConfigurationManager.TailwindLogo, null),
                                new("screens", "screens", "screens", ProjectConfigurationManager.TailwindLogo, null),
                                new("variants", "variants", "variants", ProjectConfigurationManager.TailwindLogo, null)
                            ];
                            break;
                        case "@layer":
                            completions =
                            [
                                new("base", "base", "base", ProjectConfigurationManager.TailwindLogo, null),
                                new("components", "components", "components", ProjectConfigurationManager.TailwindLogo, null),
                                new("utilities", "utilities", "utilities", ProjectConfigurationManager.TailwindLogo, null)
                            ];
                            break;
                        case "@media":
                            completions = [];
                            foreach (var screen in _projectCompletionValues.Breakpoints)
                            {
                                completions.Add(new($"screen({screen})", $"screen({screen})", $"screen({screen})", ProjectConfigurationManager.TailwindLogo, null));
                            }
                            break;
                    }
                }
                else
                {
                    if (directive == "@theme")
                    {
                        completions =
                        [
                            new("reference", "reference", "Don't emit CSS variables for these theme values.", ProjectConfigurationManager.TailwindLogo, null),
                            new("inline", "inline", "Inline these theme values into generated utilities instead of using var(…).", ProjectConfigurationManager.TailwindLogo, null),
                            new("static", "static", "Always emit these theme values into the CSS file instead of only when used.", ProjectConfigurationManager.TailwindLogo, null),
                            new("default", "default", "Allow these theme values to be overriden by JS configs and plugins.", ProjectConfigurationManager.TailwindLogo, null)
                        ];
                    }
                }
            }
            else
            {
                if (_projectCompletionValues.Version == TailwindVersion.V3)
                {
                    completions =
                    [
                        // Directive completions are hard-coded in as there are only two of them
                        new("@tailwind", "@tailwind", "Use the @tailwind directive to insert Tailwind’s base, components, utilities and variants styles into your CSS.", ProjectConfigurationManager.TailwindLogo, null),
                        new("@config", "@config", "Use the @config directive to specify which config file Tailwind should use when compiling that CSS file. This is useful for projects that need to use different configuration files for different CSS entry points.", ProjectConfigurationManager.TailwindLogo, null)
                    ];
                }
                else
                {
                    completions = [
                        new("@theme", "@theme", "Use the @theme directive to specify which config file Tailwind should use when compiling that CSS file.", ProjectConfigurationManager.TailwindLogo, null),
                        new("@source", "@source", "Use the @source directive to explicitly specify source files that aren't picked up by Tailwind's automatic content detection.", ProjectConfigurationManager.TailwindLogo, null),
                        new("@utility", "@utility", "Use the @utility directive to define a custom utility.", ProjectConfigurationManager.TailwindLogo, null),
                        new("@custom-variant", "@custom-variant", "Use the @custom-variant directive to add a custom variant in your project.", ProjectConfigurationManager.TailwindLogo, null),
                        new("@config", "@config", "Use the @config directive to specify which config file Tailwind should use when compiling that CSS file.", ProjectConfigurationManager.TailwindLogo, null),
                        new("@plugin", "@plugin", "Use the @plugin directive to include a JS plugin in your Tailwind CSS build.", ProjectConfigurationManager.TailwindLogo, null)
                    ];
                }
            }
        }
        else if (!isInBaseDirectiveBlock)
        {
            if (IsUsingApplyDirective(session))
            {
                var classRaw = applicableTo.GetText(snapshot).Split(["@apply"], StringSplitOptions.None).Last().TrimStart().Split(' ').Last() ?? "";

                completions = GetCompletions(classRaw);
            }
            else if (IsInsideSelectorBlock(session))
            {
                completions.Add(
                    // @apply completion is the only completion in this context
                    new Completion("@apply", "@apply", "Use @apply to inline any existing utility classes into your own custom CSS.", ProjectConfigurationManager.TailwindLogo, null)
                );
                if (_projectCompletionValues.Version >= TailwindVersion.V4)
                {
                    completions.Add(
                        // @apply completion is the only completion in this context
                        new Completion("@variant", "@variant", "Use the @variant directive to apply a Tailwind variant to styles in your CSS.", ProjectConfigurationManager.TailwindLogo, null)
                    );
                }
            }
            else
            {
                completions =
                [
                    new Completion("theme()", "theme()",
                    _projectCompletionValues.Version >= TailwindVersion.V4 ?
                        "Deprecated - use CSS theme variables instead." :
                        "Use the theme() function to access your Tailwind config values using dot notation.",
                    ProjectConfigurationManager.TailwindLogo, null)
                ];
            }
        }
        else if (IsCaretInBlock(session, out _, "@theme") && _projectCompletionValues.Version >= TailwindVersion.V4)
        {
            var text = line.GetText();

            if (text is null || text.Contains(':'))
            {
                return;
            }

            completions =
                [.._unsetProjectCompletionValues.ThemeStems
                .Select(t =>
                    new Completion(t, t, "", ProjectConfigurationManager.TailwindLogo, null)
                )];
        }

        if (completionSets.Count == 1)
        {
            var defaultCompletionSet = completionSets[0];

            if (defaultCompletionSet.Completions.Count > 0)
            {
                var addToBeginning = ThreadHelper.JoinableTaskFactory.Run(General.GetLiveInstanceAsync).TailwindCompletionsComeFirst;

                if (addToBeginning)
                {
                    // Cast to Completion3 to gain access to IconMoniker
                    // Return new Completion3 so session commit will actually commit the text
                    completions.AddRange(defaultCompletionSet.Completions
                        .Cast<Completion3>()
                        .Select(c => new Completion3(c.DisplayText, c.InsertionText, c.Description, new ImageMoniker() { Guid = c.IconMoniker.Guid, Id = c.IconMoniker.Id }, c.IconAutomationText)));
                }
                else
                {
                    completions.InsertRange(0, defaultCompletionSet.Completions
                        .Cast<Completion3>()
                        .Select(c => new Completion3(c.DisplayText, c.InsertionText, c.Description, new ImageMoniker() { Guid = c.IconMoniker.Guid, Id = c.IconMoniker.Id }, c.IconAutomationText)));
                }
            }

            var overridenCompletionSet = new TailwindCssCompletionSet(
                defaultCompletionSet.Moniker,
                defaultCompletionSet.DisplayName,
                applicableTo,
                completions,
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
                []));
        }
    }

    private ITrackingSpan GetApplicableTo(SnapshotPoint triggerPoint, ITextSnapshot snapshot)
    {
        SnapshotPoint end = triggerPoint;
        SnapshotPoint start;
        if (end.Position == 0)
        {
            start = end;
        }
        else
        {
            start = triggerPoint - 1;
        }

        while (start.Position > 0 && start.GetChar() != '"' && !char.IsWhiteSpace(start.GetChar()))
        {
            start -= 1;
        }

        if (start.Position != 0)
        {
            start += 1;
        }

        return snapshot.CreateTrackingSpan(new SnapshotSpan(start, end), SpanTrackingMode.EdgeInclusive);
    }

    private bool IsCaretInBlock(ICompletionSession session, out bool isInBaseDirectiveBlock, params string[] directives)
    {
        var startPos = new SnapshotPoint(session.TextView.TextSnapshot, 0);
        var caretPos = session.TextView.Caret.Position.BufferPosition;

        var searchSnapshot = new SnapshotSpan(startPos, caretPos);
        var text = searchSnapshot.GetText();

        var lastIndexOfOpenBrace = text.LastIndexOf('{');
        var lastIndexOfCloseBrace = text.LastIndexOf('}');

        var lastIndexOfCloserDirective = directives.Max(text.LastIndexOf);

        // If no open brace ('{') is found or close brace ('{') is closer to caret than open brace
        // AND also there are no @media or @layer directives
        if ((lastIndexOfOpenBrace == -1 || lastIndexOfCloseBrace > lastIndexOfOpenBrace) && lastIndexOfCloserDirective == -1)
        {
            isInBaseDirectiveBlock = false;
            return false;
        }
        else if (lastIndexOfCloserDirective != -1)
        {
            var indexOfOpeningBraceClosestToCloserDirective = text.IndexOf('{', lastIndexOfCloserDirective);

            if (indexOfOpeningBraceClosestToCloserDirective == lastIndexOfOpenBrace)
            {
                isInBaseDirectiveBlock = true;
                return true;
            }
            else
            {
                if (lastIndexOfCloseBrace < lastIndexOfOpenBrace)
                {
                    isInBaseDirectiveBlock = false;
                    return true;
                }

                // Caret is after @ directive and before the opening brace
                if (lastIndexOfCloserDirective < caretPos.Position && indexOfOpeningBraceClosestToCloserDirective == -1)
                {
                    isInBaseDirectiveBlock = false;
                    return false;
                }

                // There is another declaration in the @media/@layer block
                // Therefore, check for two closing braces in a row to determine if the block is ended

                isInBaseDirectiveBlock = true;

                var indexOfSecondToLastCloseBrace = text.LastIndexOf('}', lastIndexOfCloseBrace - 1);

                if (indexOfSecondToLastCloseBrace == -1)
                {
                    return true;
                }

                return !string.IsNullOrWhiteSpace(text.Substring(indexOfSecondToLastCloseBrace, lastIndexOfCloseBrace - indexOfSecondToLastCloseBrace));
            }
        }
        else
        {
            isInBaseDirectiveBlock = false;
            // If no close brace is found or open brace is after close brace
            return (lastIndexOfCloseBrace < lastIndexOfOpenBrace);
        }
    }

    private bool IsInsideSelectorBlock(ICompletionSession session)
    {
        // Function is called and ensured that the caret is inside a block

        var startPos = new SnapshotPoint(session.TextView.TextSnapshot, 0);
        var caretPos = session.TextView.Caret.Position.BufferPosition;

        var searchSnapshot = new SnapshotSpan(startPos, caretPos);
        var text = searchSnapshot.GetText();

        var lastIndexOfSemicolon = text.LastIndexOf(";");
        var lastIndexOfOpeningBrace = text.LastIndexOf("{");

        if (lastIndexOfSemicolon == -1 && lastIndexOfOpeningBrace == -1)
        {
            return false;
        }
        else if (lastIndexOfOpeningBrace > lastIndexOfSemicolon)
        {
            return true;
        }
        else
        {
            return text.EndsWith(";") || string.IsNullOrWhiteSpace(text.Substring(lastIndexOfSemicolon + 1));
        }
    }

    private bool IsUsingApplyDirective(ICompletionSession session)
    {
        return IsUsingDirective(session, out var directive) && directive == "@apply";
    }

    private bool IsUsingDirective(ICompletionSession session, out string? directive)
    {
        var startPos = new SnapshotPoint(session.TextView.TextSnapshot, 0);
        var caretPos = session.TextView.Caret.Position.BufferPosition;

        var searchSnapshot = new SnapshotSpan(startPos, caretPos);
        var text = searchSnapshot.GetText();

        var lastIndexOfSemicolon = text.LastIndexOf(";");
        var lastIndexOfAt = text.LastIndexOf('@');

        if (lastIndexOfAt != -1 && lastIndexOfAt > lastIndexOfSemicolon)
        {
            directive = text.Substring(lastIndexOfAt).Split(' ')[0];

            return text.EndsWith(directive) == false;
        }
        else
        {
            directive = null;
            return false;
        }
    }

    protected override async Task OnConfigurationUpdatedAsync()
    {
        // _projectCompletionValues is non-null as per base definition
        _unsetProjectCompletionValues = await _projectCompletionInit.GetUnsetCompletionConfigurationAsync(_projectCompletionValues!.Version);
    }
}
