using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using TailwindCSSIntellisense.ClassOperations;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Linting;

[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class LinterUtilities : IDisposable
{
    private readonly SemaphoreSlim _cacheSemaphore = new(1, 1);
    private readonly ProjectConfigurationManager _projectConfigurationManager;
    private readonly DescriptionGenerator _descriptionGenerator;
    private readonly ClassSortUtilities _classSortUtilities;
    private readonly CompletionConfiguration _completionConfiguration;
    private readonly SettingsProvider _settingsProvider;
    private readonly Dictionary<
        ProjectCompletionValues,
        Dictionary<string, string>
    > _cacheCssAttributes = [];

    private Linter? _linterOptions;
    private General? _generalOptions;
    private readonly Dictionary<TailwindVersion, Dictionary<string, int>> _classOrderCache = [];

    private TailwindSettings? _settings;

    [ImportingConstructor]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget is ok"
    )]
    public LinterUtilities(
        ProjectConfigurationManager completionUtilities,
        DescriptionGenerator descriptionGenerator,
        ClassSortUtilities classSortUtilities,
        CompletionConfiguration completionConfiguration,
        SettingsProvider settingsProvider
    )
    {
        _projectConfigurationManager = completionUtilities;
        _descriptionGenerator = descriptionGenerator;
        _classSortUtilities = classSortUtilities;
        _completionConfiguration = completionConfiguration;
        _settingsProvider = settingsProvider;
        Linter.Saved += LinterSettingsChanged;
        General.Saved += GeneralSettingsChanged;
        _completionConfiguration.ConfigurationUpdated += ConfigurationUpdatedAsync;
        _settingsProvider.OnSettingsChanged += LocalSettingsChangedAsync;

        ThreadHelper
            .JoinableTaskFactory.RunAsync(async () =>
            {
                _settings = await _settingsProvider.GetSettingsAsync();
            })
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/LinterUtilities/Constructor");
    }

    /// <summary>
    /// Generates errors for a list of classes.
    /// </summary>
    /// <param name="classes">The list of classes to check</param>
    /// <param name="projectCompletionValues">The project completion configuration.</param>
    /// <returns>A list of Tuples containing the class name, error message, and conflicting classes</returns>
    public async IAsyncEnumerable<(
        string className,
        string errorMessage,
        IEnumerable<string> conflictingClasses
    )> CheckForClassDuplicatesAsync(
        IEnumerable<string> classes,
        ProjectCompletionValues projectCompletionValues
    )
    {
        var classList = classes.ToList();
        Dictionary<string, int> classOrder;

        await _cacheSemaphore.WaitAsync();
        try
        {
            if (!_classOrderCache.TryGetValue(projectCompletionValues.Version, out classOrder))
            {
                _classOrderCache[projectCompletionValues.Version] = classOrder =
                    await _classSortUtilities.GetClassOrderAsync(projectCompletionValues.Version);
            }
        }
        finally
        {
            _cacheSemaphore.Release();
        }

        var cssAttributes = new Dictionary<string, string>();
        foreach (var c in classList)
        {
            var classTrimmed = c.Split(':')
                .Last()
                .Trim()
                .Replace("@@", "@")
                .Replace("@(\"@\")", "@");

            if (ImportantModifierHelper.IsImportantModifier(classTrimmed))
            {
                classTrimmed = classTrimmed.Trim('!');
            }

            // Do not handle prefix here; DescriptionGenerator.GetDescription already does

            await _cacheSemaphore.WaitAsync();
            try
            {
                if (
                    !_cacheCssAttributes.TryGetValue(projectCompletionValues, out var dict)
                    || !dict.ContainsKey(classTrimmed)
                )
                {
                    var desc = _descriptionGenerator.GetDescription(
                        classTrimmed,
                        projectCompletionValues,
                        shouldFormat: false
                    );

                    if (string.IsNullOrWhiteSpace(desc) || desc == ";")
                    {
                        continue;
                    }

                    if (dict is null)
                    {
                        _cacheCssAttributes[projectCompletionValues] = [];
                    }

                    _cacheCssAttributes[projectCompletionValues][classTrimmed] = string.Join(
                        ",",
                        desc!
                            .Split([';'], StringSplitOptions.RemoveEmptyEntries)
                            .Select(a => a.Split(':')[0].Trim())
                            .OrderBy(x => x)
                    );
                }

                cssAttributes[c] = _cacheCssAttributes[projectCompletionValues][classTrimmed];
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        foreach (var group in cssAttributes.GroupBy(x => x.Value, x => x.Key))
        {
            var erroneous = classList.Where(group.Contains).ToList();
            var count = erroneous.Count;
            if (count > 1)
            {
                var mostPrecedencePairs = classOrder
                    .Where(c => erroneous.Any(e => e.Split(':').Last().Trim() == c.Key))
                    .OrderBy(c => c.Value)
                    .ToList();

                // The sort order is the same as the order in which Tailwind generates classes
                string? classWithMostPrecedence = null;
                if (mostPrecedencePairs.Count > 0)
                {
                    classWithMostPrecedence = classList.First(e =>
                        e.Split(':').Last().Trim() == mostPrecedencePairs.First().Key
                    );
                }

                int i = 0;
                foreach (var className in erroneous)
                {
                    var others = erroneous
                        .Take(i)
                        .Concat(erroneous.Skip(i + 1).Take(count - i - 1))
                        .ToArray();

                    var errorMessage =
                        $"'{className}' applies the same CSS properties as "
                        + $"{others.Select(c => $"'{c}'").JoinWithCommasAndAnd()}.";

                    if (classWithMostPrecedence is not null)
                    {
                        errorMessage += "\n";
                        if (className == classWithMostPrecedence)
                        {
                            errorMessage += $"'{className}' styles will override others.";
                        }
                        else
                        {
                            errorMessage +=
                                $"'{className}' styles will be overriden by '{classWithMostPrecedence}'.";
                        }
                    }
                    yield return new(className, errorMessage, others);
                    i++;
                }
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "Not expensive"
    )]
    public bool LinterEnabled()
    {
        _linterOptions ??= ThreadHelper.JoinableTaskFactory.Run(Linter.GetLiveInstanceAsync);
        _generalOptions ??= ThreadHelper.JoinableTaskFactory.Run(General.GetLiveInstanceAsync);

        return _linterOptions.Enabled
            && _generalOptions.UseTailwindCss
            && _settings is not null
            && _settings.ConfigurationFiles.Count > 0;
    }

    private void LinterSettingsChanged(Linter linter)
    {
        _linterOptions = linter;
    }

    private void GeneralSettingsChanged(General general)
    {
        _generalOptions = general;
    }

    private Task LocalSettingsChangedAsync(TailwindSettings settings)
    {
        _settings = settings;
        return Task.CompletedTask;
    }

    private async Task ConfigurationUpdatedAsync()
    {
        var projectCompletionValues =
            await _projectConfigurationManager.GetAllProjectCompletionValuesAsync();

        HashSet<TailwindVersion> versionsToFind;

        await _cacheSemaphore.WaitAsync();
        try
        {
            versionsToFind =
            [
                .. projectCompletionValues
                    .Select(p => p.Version)
                    .Where(v => !_classOrderCache.ContainsKey(v)),
            ];
        }
        finally
        {
            _cacheSemaphore.Release();
        }

        var toAdd = new Dictionary<TailwindVersion, Dictionary<string, int>>();
        foreach (var version in versionsToFind)
        {
            toAdd[version] = await _classSortUtilities.GetClassOrderAsync(version);
        }

        await _cacheSemaphore.WaitAsync();
        try
        {
            foreach (var kv in toAdd)
            {
                _classOrderCache[kv.Key] = kv.Value;
            }
            _cacheCssAttributes.Clear();
        }
        finally
        {
            _cacheSemaphore.Release();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "Not expensive"
    )]
    public ErrorSeverity GetErrorSeverity(ErrorType type)
    {
        _linterOptions ??= ThreadHelper.JoinableTaskFactory.Run(Linter.GetLiveInstanceAsync);

        return type switch
        {
            ErrorType.InvalidScreen => _linterOptions.InvalidScreen,
            ErrorType.InvalidTailwindDirective => _linterOptions.InvalidTailwindDirective,
            ErrorType.InvalidConfigPath => _linterOptions.InvalidConfigPath,
            ErrorType.CssConflict => _linterOptions.CssConflict,
            ErrorType.DeprecatedAtRule => _linterOptions.DeprecatedAtRule,
            ErrorType.UsedBlocklistClass => _linterOptions.UsedBlocklistClass,
            ErrorType.InvalidSource => _linterOptions.InvalidSource,
            ErrorType.SuggestCanonicalClasses => _linterOptions.SuggestCanonicalClasses,
            _ => ErrorSeverity.Warning,
        };
    }

    public ITagSpan<IErrorTag>? CreateTagSpan(SnapshotSpan span, string error, ErrorType type)
    {
        var severity = GetErrorSeverity(type);

        if (severity == ErrorSeverity.None)
        {
            return null;
        }

        return new TagSpan<IErrorTag>(
            span,
            new ErrorTag(
                GetErrorTagFromSeverity(severity),
                new ContainerElement(
                    ContainerElementStyle.Wrapped,
                    new ImageElement(KnownMonikers.StatusWarning.ToImageId()),
                    new ClassifiedTextElement(
                        new ClassifiedTextRun(
                            PredefinedClassificationTypeNames.FormalLanguage,
                            error + " "
                        ),
                        new ClassifiedTextRun(
                            PredefinedClassificationTypeNames.ExcludedCode,
                            $"({type.ToString().ToLower()[0]}{type.ToString().Substring(1)})"
                        )
                    )
                )
            )
        );
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
        _settingsProvider.OnSettingsChanged -= LocalSettingsChangedAsync;
    }
}
