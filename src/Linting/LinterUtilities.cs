using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.ClassSort;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;

namespace TailwindCSSIntellisense.Linting;

[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class LinterUtilities : IDisposable
{
    private readonly object _cacheLock = new();
    private readonly ProjectConfigurationManager _projectConfigurationManager;
    private readonly DescriptionGenerator _descriptionGenerator;
    private readonly ClassSortUtilities _classSortUtilities;
    private readonly CompletionConfiguration _completionConfiguration;
    private readonly Dictionary<ProjectCompletionValues, Dictionary<string, string>> _cacheCssAttributes = [];

    private Linter? _linterOptions;
    private General? _generalOptions;
    private readonly Dictionary<TailwindVersion, Dictionary<string, int>> _classOrderCache = [];

    [ImportingConstructor]
    public LinterUtilities(ProjectConfigurationManager completionUtilities, DescriptionGenerator descriptionGenerator, ClassSortUtilities classSortUtilities, CompletionConfiguration completionConfiguration)
    {
        _projectConfigurationManager = completionUtilities;
        _descriptionGenerator = descriptionGenerator;
        _classSortUtilities = classSortUtilities;
        _completionConfiguration = completionConfiguration;
        Linter.Saved += LinterSettingsChanged;
        General.Saved += GeneralSettingsChanged;
        _completionConfiguration.ConfigurationUpdated += ConfigurationUpdatedAsync;
    }

    /// <summary>
    /// Generates errors for a list of classes.
    /// </summary>
    /// <param name="classes">The list of classes to check</param>
    /// <returns>A list of Tuples containing the class name and error message</returns>
    public IEnumerable<Tuple<string, string>> CheckForClassDuplicates(IEnumerable<string> classes, ProjectCompletionValues projectCompletionValues)
    {
        Dictionary<string, int> classOrder;
        lock (_cacheLock)
        {
            if (!_classOrderCache.TryGetValue(projectCompletionValues.Version, out classOrder))
            {

#pragma warning disable VSTHRD102 // Implement internal logic asynchronously
                // For most cases, this will be fine since it'll likely be populated in advance by the ConfigurationUpdatedAsync method.
                // In the case it isn't, it's better to block and get the correct result than to return an incorrect result by continuing without it.
                _classOrderCache[projectCompletionValues.Version] = classOrder = ThreadHelper.JoinableTaskFactory.Run(
                    () => _classSortUtilities.GetClassOrderAsync(projectCompletionValues.Version));
#pragma warning restore VSTHRD102 // Implement internal logic asynchronously
            }
        }

        var cssAttributes = new Dictionary<string, string>();
        foreach (var c in classes)
        {
            var classTrimmed = c.Split(':').Last().Trim().Replace("@@", "@").Replace("@(\"@\")", "@");

            if (ImportantModifierHelper.IsImportantModifier(classTrimmed))
            {
                classTrimmed = classTrimmed.Trim('!');
            }

            // Do not handle prefix here; DescriptionGenerator.GetDescription already does

            lock (_cacheLock)
            {
                if (_cacheCssAttributes.TryGetValue(projectCompletionValues, out var dict) == false || !dict.ContainsKey(classTrimmed))
                {
                    var desc = _descriptionGenerator.GetDescription(classTrimmed, projectCompletionValues, shouldFormat: false);

                    if (string.IsNullOrWhiteSpace(desc) || desc == ";")
                    {
                        continue;
                    }

                    if (dict is null)
                    {
                        _cacheCssAttributes[projectCompletionValues] = [];
                    }

                    _cacheCssAttributes[projectCompletionValues][classTrimmed] = string.Join(",", desc!.Split([';'], StringSplitOptions.RemoveEmptyEntries).Select(a => a.Split(':')[0].Trim()).OrderBy(x => x));
                }

                cssAttributes[c] = _cacheCssAttributes[projectCompletionValues][classTrimmed];
            }
        }

        foreach (var group in cssAttributes.GroupBy(x => x.Value, x => x.Key))
        {
            var erroneous = classes.Where(group.Contains);
            var count = erroneous.Count();
            if (count > 1)
            {
                var mostPrecedencePairs =
                    classOrder
                        .Where(c => erroneous.Any(e => e.Split(':').Last().Trim() == c.Key))
                        .OrderBy(c => c.Value);

                // The sort order is the same as the order in which Tailwind generates classes
                string? classWithMostPrecedence = null;
                if (mostPrecedencePairs.Any())
                {
                    classWithMostPrecedence = classes.Where(e => e.Split(':').Last().Trim() == mostPrecedencePairs.First().Key)
                        .First();
                }

                int i = 0;
                foreach (var className in erroneous)
                {
                    var others = erroneous.Take(i)
                        .Concat(erroneous.Skip(i + 1).Take(count - i - 1))
                        .Select(c => $"'{c}'");

                    var errorMessage =
                        $"'{className}' applies the same CSS properties as " +
                        $"{string.Join(", ", others.Take(count - 2))}" +
                        $"{(count > 2 ? " and " : "")}" +
                        $"{others.Last()}.";

                    if (classWithMostPrecedence is not null)
                    {
                        errorMessage += "\n";
                        if (className == classWithMostPrecedence)
                        {
                            errorMessage += $"'{className}' styles will override others.";
                        }
                        else
                        {
                            errorMessage += $"'{className}' styles will be overriden by '{classWithMostPrecedence}'.";
                        }

                    }
                    yield return new(className, errorMessage);
                    i++;
                }
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously", Justification = "Not expensive")]
    public bool LinterEnabled()
    {
        _linterOptions ??= ThreadHelper.JoinableTaskFactory.Run(Linter.GetLiveInstanceAsync);
        _generalOptions ??= ThreadHelper.JoinableTaskFactory.Run(General.GetLiveInstanceAsync);

        return _linterOptions.Enabled && _generalOptions.UseTailwindCss;
    }

    private void LinterSettingsChanged(Linter linter)
    {
        _linterOptions = linter;
    }

    private void GeneralSettingsChanged(General general)
    {
        _generalOptions = general;
    }

    private async Task ConfigurationUpdatedAsync()
    {
        var projectCompletionValues = await _projectConfigurationManager.GetAllProjectCompletionValuesAsync();

        HashSet<TailwindVersion> versionsToFind;

        lock (_cacheLock)
        {
            versionsToFind = [.. projectCompletionValues.Select(p => p.Version).Where(v => !_classOrderCache.ContainsKey(v))];
        }

        var toAdd = new Dictionary<TailwindVersion, Dictionary<string, int>>();
        foreach (var version in versionsToFind)
        {
            toAdd[version] = await _classSortUtilities.GetClassOrderAsync(version);
        }

        lock (_cacheLock)
        {
            foreach (var kv in toAdd)
            {
                _classOrderCache[kv.Key] = kv.Value;
            }
            _cacheCssAttributes.Clear();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously", Justification = "Not expensive")]
    public ErrorSeverity GetErrorSeverity(ErrorType type)
    {
        _linterOptions ??= ThreadHelper.JoinableTaskFactory.Run(Linter.GetLiveInstanceAsync);

        return type switch
        {
            ErrorType.InvalidScreen => _linterOptions.InvalidScreen,
            ErrorType.InvalidTailwindDirective => _linterOptions.InvalidTailwindDirective,
            ErrorType.InvalidConfigPath => _linterOptions.InvalidConfigPath,
            ErrorType.CssConflict => _linterOptions.CssConflict,
            _ => ErrorSeverity.Warning
        };
    }

    public ITagSpan<IErrorTag>? CreateTagSpan(SnapshotSpan span, string error, ErrorType type)
    {
        var severity = GetErrorSeverity(type);

        if (severity == ErrorSeverity.None)
        {
            return null;
        }

        return new TagSpan<IErrorTag>(span, new ErrorTag(GetErrorTagFromSeverity(severity),
            new ContainerElement(ContainerElementStyle.Wrapped,
                new ImageElement(KnownMonikers.StatusWarning.ToImageId()),
                new ClassifiedTextElement(
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.FormalLanguage, error + " "),
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.ExcludedCode, $"({type.ToString().ToLower()[0]}{type.ToString().Substring(1)})")
                )
            )
        ));
    }

    public string GetErrorTagFromSeverity(ErrorSeverity severity)
    {
        return severity switch
        {
            ErrorSeverity.Suggestion => PredefinedErrorTypeNames.HintedSuggestion,
            ErrorSeverity.Warning => PredefinedErrorTypeNames.Warning,
            ErrorSeverity.Error => PredefinedErrorTypeNames.SyntaxError,
            _ => PredefinedErrorTypeNames.OtherError,
        };
    }

    public void Dispose()
    {
        Linter.Saved -= LinterSettingsChanged;
        General.Saved -= GeneralSettingsChanged;
        _completionConfiguration.ConfigurationUpdated -= ConfigurationUpdatedAsync;
    }
}