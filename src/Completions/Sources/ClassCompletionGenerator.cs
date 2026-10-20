using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Threading;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions.Sources;

internal abstract class ClassCompletionGenerator : IDisposable
{
    protected readonly ProjectConfigurationManager _projectCompletionManager;
    protected readonly ProjectConfigurationInitializer _projectCompletionInit;
    protected ProjectCompletionValues? _projectCompletionValues;
    protected readonly ColorIconGenerator _colorIconGenerator;
    protected readonly DescriptionGenerator _descriptionGenerator;
    protected readonly SettingsProvider _settingsProvider;
    private readonly CompletionConfiguration _completionConfiguration;
    protected readonly ITextBuffer _textBuffer;

    protected bool? _showAutocomplete;
    protected TailwindSettings? _settings;

    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    protected ClassCompletionGenerator(
        ITextBuffer textBuffer,
        ProjectConfigurationManager completionUtils,
        ColorIconGenerator colorIconGenerator,
        DescriptionGenerator descriptionGenerator,
        SettingsProvider settingsProvider,
        CompletionConfiguration completionConfiguration,
        ProjectConfigurationInitializer projectCompletionInit
    )
    {
        _textBuffer = textBuffer;
        _projectCompletionManager = completionUtils;
        _colorIconGenerator = colorIconGenerator;
        _descriptionGenerator = descriptionGenerator;
        _settingsProvider = settingsProvider;
        _completionConfiguration = completionConfiguration;
        _settingsProvider.OnSettingsChanged += SettingsChangedAsync;
        _completionConfiguration.ConfigurationUpdated += ReloadProjectCompletionValuesAsync;
        _projectCompletionInit = projectCompletionInit;
    }

    /// <summary>
    /// Must call in child class constructors.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget is ok"
    )]
    protected void Initialize()
    {
        // Set _projectConfigurationValues without blocking
        ThreadHelper
            .JoinableTaskFactory.RunAsync(async () =>
            {
                await ReloadProjectCompletionValuesAsync();
                _settings = await _settingsProvider.GetSettingsAsync();
            })
            .FileAndForget(
                nameof(TailwindCSSIntellisense) + "/ClassCompletionGenerator/Initialize"
            );
    }

    /// <summary>
    /// Gets relevant Tailwind CSS completions for a certain input
    /// </summary>
    /// <param name="classRaw">The current class text from the caret to the previous break area (i.e. ' ')</param>
    /// <param name="isRazor">Whether the completions are for a Razor document.</param>
    /// <returns>A list of completions</returns>
    protected List<Completion> GetCompletions(string classRaw, bool isRazor = false)
    {
        if (_projectCompletionValues is null)
        {
            return [];
        }

        var prefix = _projectCompletionValues.Prefix;

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            if (
                !string.IsNullOrWhiteSpace(classRaw)
                && !classRaw.StartsWith(prefix)
                && _projectCompletionValues.Version >= TailwindVersion.V4
            )
            {
                return [];
            }
        }
        else
        {
            prefix = "";
        }

        var variants = classRaw.SplitClassVariants();

        var currentClass = variants.Last();

        var isImportant = ImportantModifierHelper.IsImportantModifier(currentClass);

        var suffix = "";

        if (isImportant)
        {
            currentClass = currentClass.Trim('!');

            if (_projectCompletionValues.Version >= TailwindVersion.V4)
            {
                suffix = "!";
            }
        }
        else if (currentClass.StartsWith("!") || currentClass.EndsWith("!"))
        {
            return [];
        }

        if (_projectCompletionValues.Version == TailwindVersion.V3)
        {
            if (isImportant)
            {
                prefix = $"!{prefix}";
            }

            if (!string.IsNullOrWhiteSpace(currentClass) && !string.IsNullOrWhiteSpace(prefix))
            {
                if (currentClass.StartsWith(prefix!))
                {
                    currentClass = currentClass.Substring(prefix!.Length);
                }
                else if (currentClass.StartsWith($"-{prefix}"))
                {
                    currentClass = $"-{currentClass.Substring(prefix!.Length + 1)}";
                }
            }
        }

        variants.RemoveAt(variants.Count - 1);

        var completions = new List<Completion>();

        // If this is in v4+, also contains the prefix
        var variantsAsString = string.Join(":", variants);
        var v4PrefixToInsert = "";
        if (!string.IsNullOrWhiteSpace(variantsAsString))
        {
            variantsAsString += ":";
        }

        // v4 prefixes act as the first variant (tw:hover:block). v3 prefixes are added
        // to each utility below (hover:tw-block), so inserting them here would duplicate them.
        if (
            _projectCompletionValues.Version >= TailwindVersion.V4
            && (string.IsNullOrWhiteSpace(variantsAsString) || !variantsAsString.StartsWith(prefix))
        )
        {
            v4PrefixToInsert += prefix;
        }

        // For use to check if it matches part of the blocklist
        var variantsAsStringUnescaped = variantsAsString;
        if (isRazor)
        {
            variantsAsStringUnescaped = variantsAsString
                .Replace("@@", "@")
                .Replace("@(\"@\")", "@");
        }

        IEnumerable<TailwindClass> scope = _projectCompletionValues.Classes;

        if (!currentClass.StartsWith("-"))
        {
            scope = scope.OrderBy(c => c.Name.StartsWith("-"));
        }

        foreach (var twClass in scope)
        {
            if (twClass.UseColors)
            {
                IEnumerable<string> colors;

                if (
                    _projectCompletionValues.CustomColorMappers != null
                    && _projectCompletionValues.CustomColorMappers.TryGetValue(
                        twClass.Name,
                        out var customColors
                    )
                )
                {
                    colors = customColors.Keys;
                }
                else
                {
                    colors = _projectCompletionValues.ColorMapper.Keys;
                }

                foreach (var color in colors)
                {
                    var className = string.Format(twClass.Name, color);
                    var classNameRaw = className;

                    if (
                        _projectCompletionValues.Version == TailwindVersion.V3
                        && !string.IsNullOrWhiteSpace(prefix)
                    )
                    {
                        if (className.StartsWith("-"))
                        {
                            className = $"-{prefix}{className.TrimStart('-')}";
                        }
                        else
                        {
                            className = $"{prefix}{className}";
                        }
                    }

                    if (
                        !_projectCompletionValues.IsClassAllowed(
                            variantsAsStringUnescaped + className + suffix
                        )
                    )
                    {
                        continue;
                    }

                    var insert =
                        v4PrefixToInsert
                        + variantsAsString
                        + (isRazor ? className.Replace("@", "@@") : className)
                        + suffix;

                    completions.Add(
                        new Completion(
                            className + suffix,
                            insert,
                            classNameRaw,
                            _colorIconGenerator.GetImageFromColor(
                                _projectCompletionValues,
                                twClass.Name,
                                color,
                                color == "transparent" ? 0 : 100
                            ),
                            null
                        )
                    );

                    if (
                        twClass.UseOpacity
                        && currentClass.Contains(color)
                        && currentClass.Contains('/')
                        && currentClass.StartsWith(className)
                    )
                    {
                        foreach (var opacity in _projectCompletionInit.Opacity)
                        {
                            if (
                                !_projectCompletionValues.IsClassAllowed(
                                    $"{variantsAsStringUnescaped}{className}/{opacity}{suffix}"
                                )
                            )
                            {
                                continue;
                            }

                            insert =
                                $"{v4PrefixToInsert}{variantsAsString}{(isRazor ? className.Replace("@", "@@") : className)}/{opacity}{suffix}";

                            completions.Add(
                                new Completion(
                                    $"{className}/{opacity}{suffix}",
                                    insert,
                                    $"{className}/{opacity}",
                                    _colorIconGenerator.GetImageFromColor(
                                        _projectCompletionValues,
                                        twClass.Name,
                                        color,
                                        opacity
                                    ),
                                    null
                                )
                            );
                        }

                        insert =
                            v4PrefixToInsert
                            + variantsAsString
                            + (isRazor ? className.Replace("@", "@@") : className)
                            + "/[]"
                            + suffix;
                        completions.Add(
                            new Completion(
                                className + "/[]" + suffix,
                                insert,
                                className + "/[]",
                                ProjectConfigurationManager.TailwindLogo,
                                null
                            )
                        );
                    }
                }
            }
            else if (twClass.UseSpacing)
            {
                IEnumerable<string> spacings;

                if (
                    _projectCompletionValues.CustomSpacingMappers != null
                    && _projectCompletionValues.CustomSpacingMappers.TryGetValue(
                        twClass.Name,
                        out var value
                    )
                )
                {
                    spacings = value.Keys;
                }
                else
                {
                    spacings = _projectCompletionValues.SpacingMapper.Keys;
                }

                foreach (var spacing in spacings)
                {
                    var className = string.IsNullOrWhiteSpace(spacing)
                        ? twClass.Name.Replace("-{0}", "")
                        : string.Format(twClass.Name, spacing);
                    var classNameRaw = className;

                    if (
                        _projectCompletionValues.Version == TailwindVersion.V3
                        && !string.IsNullOrWhiteSpace(prefix)
                    )
                    {
                        if (className.StartsWith("-"))
                        {
                            className = $"-{prefix}{className.TrimStart('-')}";
                        }
                        else
                        {
                            className = $"{prefix}{className}";
                        }
                    }

                    if (
                        !_projectCompletionValues.IsClassAllowed(
                            variantsAsStringUnescaped + className + suffix
                        )
                    )
                    {
                        continue;
                    }

                    var insert =
                        v4PrefixToInsert
                        + variantsAsString
                        + (isRazor ? className.Replace("@", "@@") : className)
                        + suffix;
                    completions.Add(
                        new Completion(
                            className + suffix,
                            insert,
                            classNameRaw,
                            ProjectConfigurationManager.TailwindLogo,
                            null
                        )
                    );
                }
            }
            else if (twClass.HasArbitrary)
            {
                var className = twClass.Name;
                var classNameRaw = className;

                if (
                    _projectCompletionValues.Version == TailwindVersion.V3
                    && !string.IsNullOrWhiteSpace(prefix)
                )
                {
                    if (className.StartsWith("-"))
                    {
                        className = $"-{prefix}{className.TrimStart('-')}";
                    }
                    else
                    {
                        className = $"{prefix}{className}";
                    }
                }

                if (
                    !_projectCompletionValues.IsClassAllowed(
                        variantsAsStringUnescaped + className + suffix
                    )
                )
                {
                    continue;
                }

                var insert =
                    v4PrefixToInsert
                    + variantsAsString
                    + (isRazor ? className.Replace("@", "@@") : className)
                    + "[]"
                    + suffix;
                completions.Add(
                    new Completion(
                        className + "[]" + suffix,
                        insert,
                        classNameRaw + "[]",
                        ProjectConfigurationManager.TailwindLogo,
                        null
                    )
                );
            }
            else
            {
                var className = twClass.Name;

                if (className.Contains("{0}"))
                {
                    continue;
                }

                var classNameRaw = className;

                if (
                    _projectCompletionValues.Version == TailwindVersion.V3
                    && !string.IsNullOrWhiteSpace(prefix)
                )
                {
                    if (className.StartsWith("-"))
                    {
                        className = $"-{prefix}{className.TrimStart('-')}";
                    }
                    else
                    {
                        className = $"{prefix}{className}";
                    }
                }

                if (
                    !_projectCompletionValues.IsClassAllowed(
                        variantsAsStringUnescaped + className + suffix
                    )
                )
                {
                    continue;
                }

                var insert =
                    v4PrefixToInsert
                    + variantsAsString
                    + (isRazor ? className.Replace("@", "@@") : className)
                    + suffix;
                completions.Add(
                    new Completion(
                        className + suffix,
                        insert,
                        classNameRaw,
                        ProjectConfigurationManager.TailwindLogo,
                        null
                    )
                );

                if (currentClass.Contains('/'))
                {
                    if (
                        classNameRaw.StartsWith("bg-linear")
                        || classNameRaw.StartsWith("bg-radial")
                        || classNameRaw.StartsWith("bg-conic")
                    )
                    {
                        foreach (var modifier in KnownModifiers.GradientModifierToDescription.Keys)
                        {
                            if (
                                !_projectCompletionValues.IsClassAllowed(
                                    $"{variantsAsStringUnescaped}{className}/{modifier}{suffix}"
                                )
                            )
                            {
                                continue;
                            }

                            insert =
                                $"{v4PrefixToInsert}{variantsAsString}{(isRazor ? className.Replace("@", "@@") : className)}/{modifier}{suffix}";

                            completions.Add(
                                new Completion(
                                    $"{className}/{modifier}{suffix}",
                                    insert,
                                    $"{classNameRaw}/{modifier}",
                                    ProjectConfigurationManager.TailwindLogo,
                                    null
                                )
                            );
                        }

                        insert =
                            v4PrefixToInsert
                            + variantsAsString
                            + (isRazor ? className.Replace("@", "@@") : className)
                            + "/[]"
                            + suffix;
                        completions.Add(
                            new Completion(
                                className + "/[]" + suffix,
                                insert,
                                classNameRaw + "/[]",
                                ProjectConfigurationManager.TailwindLogo,
                                null
                            )
                        );
                    }
                    else if (
                        KnownModifiers.IsEligibleForLineHeightModifier(
                            className,
                            _projectCompletionValues
                        )
                    )
                    {
                        var lineHeightModifiers = _projectCompletionValues
                            .CssVariables.Where(v => v.Key.StartsWith("--leading-"))
                            .Select(v => v.Key.Replace("--leading-", ""));
                        foreach (var modifier in lineHeightModifiers)
                        {
                            if (
                                !_projectCompletionValues.IsClassAllowed(
                                    $"{variantsAsStringUnescaped}{className}/{modifier}{suffix}"
                                )
                            )
                            {
                                continue;
                            }

                            insert =
                                $"{v4PrefixToInsert}{variantsAsString}{(isRazor ? className.Replace("@", "@@") : className)}/{modifier}{suffix}";
                            completions.Add(
                                new Completion(
                                    $"{className}/{modifier}{suffix}",
                                    insert,
                                    $"{classNameRaw}/{modifier}",
                                    ProjectConfigurationManager.TailwindLogo,
                                    null
                                )
                            );
                        }

                        insert =
                            v4PrefixToInsert
                            + variantsAsString
                            + (isRazor ? className.Replace("@", "@@") : className)
                            + "/[]"
                            + suffix;
                        completions.Add(
                            new Completion(
                                className + "/[]" + suffix,
                                insert,
                                classNameRaw + "/[]",
                                ProjectConfigurationManager.TailwindLogo,
                                null
                            )
                        );
                    }
                }
            }
        }

        if (_projectCompletionValues.PluginClasses != null)
        {
            foreach (var pluginClass in _projectCompletionValues.PluginClasses)
            {
                if (
                    !_projectCompletionValues.IsClassAllowed(
                        variantsAsStringUnescaped + pluginClass + suffix
                    )
                )
                {
                    continue;
                }

                var insert =
                    v4PrefixToInsert
                    + variantsAsString
                    + (_projectCompletionValues.Version == TailwindVersion.V3 ? prefix : "")
                    + (isRazor ? pluginClass.Replace("@", "@@") : pluginClass).TrimStart('.')
                    + suffix;

                completions.Add(
                    new Completion(
                        pluginClass.TrimStart('.'),
                        insert,
                        pluginClass.TrimStart('.'),
                        ProjectConfigurationManager.TailwindLogo,
                        null
                    )
                );
            }
        }

        var variantCompletions = new List<Completion>();
        var variantCompletionsToAddToEnd = new List<Completion>();

        var allVariants = _projectCompletionValues.Variants.SelectMany(v =>
        {
            if (v.Contains("{b}"))
            {
                return _projectCompletionValues.Breakpoints.Keys.Select(b =>
                    $"{v.Replace("{b}", b)}"
                );
            }
            else if (v.Contains("{c}"))
            {
                return _projectCompletionValues.Containers.Keys.Select(c =>
                    $"{v.Replace("{c}", c)}"
                );
            }
            else if (v.Contains("{a}"))
            {
                return [v.Replace("{a}", "[]")];
            }
            return [v];
        });

        foreach (var variant in allVariants)
        {
            var description = _descriptionGenerator.GetVariantDescription(
                variant,
                _projectCompletionValues
            );

            if (!variants.Contains(variant))
            {
                var insert =
                    v4PrefixToInsert
                    + variantsAsString
                    + (isRazor ? variant.Replace("@", "@@") : variant)
                    + ":";

                var completion = new Completion(
                    variant + ":",
                    insert,
                    description,
                    ProjectConfigurationManager.TailwindLogo,
                    null
                );

                completion.Properties.AddProperty("variant", true);
                variantCompletions.Add(completion);

                if (
                    _projectCompletionValues.Version == TailwindVersion.V3
                    && description is not null
                    && description.StartsWith("&:")
                    && description.Substring(2) == variant
                )
                {
                    insert =
                        v4PrefixToInsert
                        + variantsAsString
                        + "group-"
                        + (isRazor ? variant.Replace("@", "@@") : variant)
                        + ":";

                    completion = new Completion(
                        "group-" + variant + ":",
                        insert,
                        _descriptionGenerator.GetVariantDescription(
                            "group-" + variant,
                            _projectCompletionValues
                        ),
                        ProjectConfigurationManager.TailwindLogo,
                        null
                    );
                    completion.Properties.AddProperty("variant", true);
                    variantCompletionsToAddToEnd.Add(completion);

                    insert =
                        v4PrefixToInsert
                        + variantsAsString
                        + "peer-"
                        + (isRazor ? variant.Replace("@", "@@") : variant)
                        + ":";
                    completion = new Completion(
                        "peer-" + variant + ":",
                        insert,
                        _descriptionGenerator.GetVariantDescription(
                            "peer-" + variant,
                            _projectCompletionValues
                        ),
                        ProjectConfigurationManager.TailwindLogo,
                        null
                    );
                    completion.Properties.AddProperty("variant", true);
                    variantCompletionsToAddToEnd.Add(completion);
                }
            }
        }

        if (_projectCompletionValues.PluginVariants != null)
        {
            foreach (var variant in _projectCompletionValues.PluginVariants)
            {
                if (!variants.Contains(variant))
                {
                    var insert =
                        v4PrefixToInsert
                        + variantsAsString
                        + (isRazor ? variant.Replace("@", "@@") : variant)
                        + ":";
                    var completion = new Completion(
                        variant + ":",
                        insert,
                        _projectCompletionValues.Version >= TailwindVersion.V4
                            ? _descriptionGenerator.GetVariantDescription(
                                variant,
                                _projectCompletionValues
                            )
                            : variant,
                        ProjectConfigurationManager.TailwindLogo,
                        null
                    );

                    completion.Properties.AddProperty("variant", true);
                    variantCompletions.Add(completion);
                }
            }
        }

        if (_projectCompletionValues.Version == TailwindVersion.V3)
        {
            foreach (var screen in _projectCompletionValues.Breakpoints.Keys)
            {
                if (!variants.Contains(screen))
                {
                    var insert =
                        v4PrefixToInsert
                        + variantsAsString
                        + (isRazor ? screen.Replace("@", "@@") : screen)
                        + ":";

                    var completion = new Completion(
                        screen + ":",
                        insert,
                        screen,
                        ProjectConfigurationManager.TailwindLogo,
                        null
                    );

                    completion.Properties.AddProperty("variant", true);
                    variantCompletions.Add(completion);

                    if (_projectCompletionValues.Version == TailwindVersion.V3)
                    {
                        insert =
                            v4PrefixToInsert
                            + variantsAsString
                            + "max-"
                            + (isRazor ? screen.Replace("@", "@@") : screen)
                            + ":";

                        completion = new Completion(
                            "max-" + screen + ":",
                            insert,
                            screen,
                            ProjectConfigurationManager.TailwindLogo,
                            null
                        );
                        completion.Properties.AddProperty("variant", true);
                        variantCompletionsToAddToEnd.Add(completion);
                    }
                }
            }
        }

        // keep peer, group, max variants at the end
        variantCompletions.AddRange(variantCompletionsToAddToEnd);

        if (string.IsNullOrWhiteSpace(currentClass))
        {
            completions.InsertRange(0, variantCompletions);
        }
        else
        {
            completions.AddRange(variantCompletions);
        }

        foreach (var completion in completions)
        {
            completion.Properties.AddProperty("tailwind", _projectCompletionValues);
        }
        return completions;
    }

    public virtual void Dispose()
    {
        _settingsProvider.OnSettingsChanged -= SettingsChangedAsync;
        _completionConfiguration.ConfigurationUpdated -= ReloadProjectCompletionValuesAsync;
    }

    private Task SettingsChangedAsync(TailwindSettings settings)
    {
        _showAutocomplete = settings.EnableTailwindCss;
        _settings = settings;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs on completion configuration update and runs AFTER _projectCompletionValues is set,
    /// so you can assume it is non-null.
    /// </summary>
    protected virtual Task OnConfigurationUpdatedAsync()
    {
        return Task.CompletedTask;
    }

    private async Task ReloadProjectCompletionValuesAsync()
    {
        await _reloadLock.WaitAsync();
        try
        {
            _projectCompletionValues =
                await _projectCompletionManager.GetCompletionConfigurationByFilePathAsync(
                    _textBuffer.GetFileNameSafe()
                );
            await OnConfigurationUpdatedAsync();
        }
        finally
        {
            _reloadLock.Release();
        }
    }
}
