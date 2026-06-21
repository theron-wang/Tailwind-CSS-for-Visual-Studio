using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Initialization;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions;

/// <summary>
/// Provides project configurations (completion data) for each configuration file.
/// </summary>
[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ProjectConfigurationInitializer
{
    [Import]
    private SettingsProvider SettingsProvider { get; set; } = null!;

    internal List<int> Opacity { get; set; } = [];

    private readonly Dictionary<
        TailwindVersion,
        UnsetProjectCompletionValues
    > _unsetProjectCompletionConfigurations = [];

    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly SemaphoreSlim _unsetConfigsLock = new(1, 1);
    private Task<bool>? _initializationTask;

    public async Task InitializeAsync()
    {
        Task<bool> task;

        await _initializeLock.WaitAsync();
        try
        {
            _initializationTask ??= InitializeImplAsync();
            task = _initializationTask;
        }
        finally
        {
            _initializeLock.Release();
        }

        bool success;
        try
        {
            success = await task;
        }
        catch
        {
            // allow retry on next call
            await _initializeLock.WaitAsync();
            try
            {
                if (_initializationTask == task)
                {
                    _initializationTask = null;
                }
            }
            finally
            {
                _initializeLock.Release();
            }

            throw;
        }

        if (!success)
        {
            // allow retry on next call
            await _initializeLock.WaitAsync();
            try
            {
                if (_initializationTask == task)
                {
                    _initializationTask = null;
                }
            }
            finally
            {
                _initializeLock.Release();
            }
        }
    }

    private async Task<bool> InitializeImplAsync()
    {
        try
        {
            if (!await ShouldInitializeAsync())
            {
                return false;
            }

            // Initial load of settings, which then triggers class loading based on the versions of the config files
            await SettingsProvider.GetSettingsAsync();

            await VS.StatusBar.ShowMessageAsync("Tailwind CSS IntelliSense initialized");

            return true;
        }
        catch (Exception ex)
        {
            await ex.LogAsync();

            await VS.StatusBar.ShowMessageAsync(
                "Tailwind CSS initialization failed: check extension output"
            );

            return false;
        }
    }

    public async Task<UnsetProjectCompletionValues> GetUnsetCompletionConfigurationAsync(
        TailwindVersion version
    )
    {
        await InitializeAsync();

        await _unsetConfigsLock.WaitAsync();

        try
        {
            if (!_unsetProjectCompletionConfigurations.ContainsKey(version))
            {
                await LoadClassesAsync(version);
            }

            return _unsetProjectCompletionConfigurations[version];
        }
        finally
        {
            _unsetConfigsLock.Release();
        }
    }

    private async Task<bool> ShouldInitializeAsync()
    {
        var settings = await SettingsProvider.GetSettingsAsync();
        return settings.ConfigurationFiles.Count > 0 || settings.BuildFiles.Count > 0;
    }

    private async Task LoadClassesV3Async()
    {
        if (_unsetProjectCompletionConfigurations.ContainsKey(TailwindVersion.V3))
        {
            return;
        }

        var result = await ResourcesLoader.LoadResourcesForVersionAsync(TailwindVersion.V3);

        var project = result.UnsetProject;

        project.Classes = [];
        Opacity = result.Opacity;

        foreach (var classType in result.ClassTypes.Cast<ClassTypeV3>())
        {
            var classes = new List<TailwindClass>();

            if (classType.DirectVariants != null && classType.DirectVariants.Count > 0)
            {
                foreach (var v in classType.DirectVariants)
                {
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        classes.Add(new TailwindClass() { Name = classType.Stem });
                    }
                    else
                    {
                        if (v.Contains("{s}"))
                        {
                            classes.Add(
                                new TailwindClass()
                                {
                                    Name = classType.Stem + "-" + v.Replace("{s}", "{0}"),
                                    UseSpacing = true,
                                }
                            );
                        }
                        else if (v.Contains("{c}"))
                        {
                            classes.Add(
                                new TailwindClass()
                                {
                                    Name = classType.Stem + "-" + v.Replace("{c}", "{0}"),
                                    UseColors = true,
                                    UseOpacity = classType.UseOpacity == true,
                                }
                            );
                        }
                        else
                        {
                            classes.Add(new TailwindClass() { Name = classType.Stem + "-" + v });
                        }
                    }
                }
            }

            if (classType.Subvariants != null && classType.Subvariants.Count > 0)
            {
                // Do the same check for each of the subvariants as above

                foreach (var subvariant in classType.Subvariants)
                {
                    if (subvariant.Variants != null)
                    {
                        foreach (var v in subvariant.Variants)
                        {
                            if (string.IsNullOrWhiteSpace(v))
                            {
                                classes.Add(
                                    new TailwindClass()
                                    {
                                        Name = classType.Stem + "-" + subvariant.Stem,
                                    }
                                );
                            }
                            else
                            {
                                classes.Add(
                                    new TailwindClass()
                                    {
                                        Name = classType.Stem + "-" + subvariant.Stem + "-" + v,
                                    }
                                );
                            }
                        }
                    }

                    if (subvariant.Stem.Contains("{c}"))
                    {
                        classes.Add(
                            new TailwindClass()
                            {
                                Name = classType.Stem + "-" + subvariant.Stem.Replace("{c}", "{0}"),
                                // Notify the completion provider to show color options
                                UseColors = true,
                                UseOpacity = classType.UseOpacity == true,
                            }
                        );
                    }
                    else if (subvariant.Stem.Contains("{s}"))
                    {
                        classes.Add(
                            new TailwindClass()
                            {
                                Name = classType.Stem + "-" + subvariant.Stem.Replace("{s}", "{0}"),
                                // Notify the completion provider to show spacing options
                                UseSpacing = true,
                            }
                        );
                    }
                }
            }

            if (
                (classType.DirectVariants == null || classType.DirectVariants.Count == 0)
                && (classType.Subvariants == null || classType.Subvariants.Count == 0)
            )
            {
                var newClass = new TailwindClass() { Name = classType.Stem };
                if (classType.UseColors == true)
                {
                    newClass.UseColors = true;
                    newClass.UseOpacity = classType.UseOpacity == true;
                    newClass.Name += "-{0}";
                }
                else if (classType.UseSpacing == true)
                {
                    newClass.UseSpacing = true;
                    newClass.Name += "-{0}";
                }
                classes.Add(newClass);
            }

            project.Classes.AddRange(classes);

            if (classType.HasNegative == true)
            {
                var negativeClasses = classes
                    .Select(c =>
                    {
                        return new TailwindClass()
                        {
                            Name = $"-{c.Name}",
                            UseColors = c.UseColors,
                            UseSpacing = c.UseSpacing,
                        };
                    })
                    .ToList();

                project.Classes.AddRange(negativeClasses);
            }
        }
        foreach (var stems in project.ConfigurationValueToClassStems.Values)
        {
            foreach (var stem in stems)
            {
                string name;
                if (stem.Contains('{'))
                {
                    var replace = stem.Substring(
                        stem.IndexOf('{'),
                        stem.IndexOf('}') - stem.IndexOf('{') + 1
                    );
                    name = stem.Replace(replace, "");
                }
                else
                {
                    name = stem.EndsWith("-") ? stem : stem + "-";
                }

                if (stem.Contains(":"))
                {
                    project.Variants.Add($"{name.Replace(":-", "")}-[]");
                }
                else
                {
                    if (
                        project.Classes.All(c =>
                            (c.Name == name && c.HasArbitrary == false) || c.Name != name
                        )
                    )
                    {
                        project.Classes.Add(
                            new TailwindClass() { Name = name, HasArbitrary = true }
                        );
                    }
                }
            }
        }

        project.Breakpoints = new Dictionary<string, string>
        {
            { "sm", "640px" },
            { "md", "768px" },
            { "lg", "1024px" },
            { "xl", "1280px" },
            { "2xl", "1536px" },
        };

        _unsetProjectCompletionConfigurations[TailwindVersion.V3] = project;
    }

    private async Task LoadClassesAsync(TailwindVersion version)
    {
        if (_unsetProjectCompletionConfigurations.ContainsKey(version))
        {
            return;
        }

        if (version == TailwindVersion.V3)
        {
            await LoadClassesV3Async();
            return;
        }

        var result = await ResourcesLoader.LoadResourcesForVersionAsync(version);

        var project = result.UnsetProject;

        project.Variants = [.. project.VariantsToDescriptions.Keys];

        project.Classes = [];
        Opacity = result.Opacity;

        var fractions = new[] { 2, 3, 4, 6, 12 }
            .SelectMany(d =>
                Enumerable.Range(1, d - 1).Select(n => new { Numerator = n, Denominator = d })
            )
            .Where(f => f.Numerator < 12)
            .Select(f => $"{f.Numerator}/{f.Denominator}")
            .ToList();

        foreach (var classType in result.ClassTypes.Cast<ClassType>())
        {
            var classes = new List<TailwindClass>();

            if (classType.DirectVariants != null && classType.DirectVariants.Count > 0)
            {
                foreach (var v in classType.DirectVariants)
                {
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        classes.Add(new TailwindClass() { Name = classType.Stem });
                    }
                    else
                    {
                        if (v.Contains("{s}"))
                        {
                            classes.Add(
                                new TailwindClass()
                                {
                                    Name = classType.Stem + "-" + v.Replace("{s}", "{0}"),
                                    UseSpacing = true,
                                }
                            );
                        }
                        else if (v.Contains("{c}"))
                        {
                            classes.Add(
                                new TailwindClass()
                                {
                                    Name = classType.Stem + "-" + v.Replace("{c}", "{0}"),
                                    UseColors = true,
                                    UseOpacity = true,
                                }
                            );
                        }
                        else if (v.Contains("{n}"))
                        {
                            classes.Add(
                                new TailwindClass()
                                {
                                    Name = classType.Stem + "-" + v.Replace("{n}", "{0}"),
                                    UseNumbers = true,
                                }
                            );
                        }
                        else if (v.Contains("{%}"))
                        {
                            classes.Add(
                                new TailwindClass()
                                {
                                    Name = classType.Stem + "-" + v.Replace("{%}", "{0}"),
                                    UsePercent = true,
                                }
                            );
                        }
                        else if (v.Contains("{f}"))
                        {
                            classes.Add(
                                new TailwindClass()
                                {
                                    Name = classType.Stem + "-" + v.Replace("{f}", "{0}"),
                                    UseFractions = true,
                                }
                            );

                            // Auto-generate fractions
                            classes.AddRange(
                                fractions.Select(f =>
                                {
                                    return new TailwindClass()
                                    {
                                        Name = classType.Stem + "-" + v.Replace("{f}", f),
                                    };
                                })
                            );
                        }
                        else
                        {
                            classes.Add(new TailwindClass() { Name = classType.Stem + "-" + v });
                        }
                    }
                }
            }

            if (classType.Subvariants != null && classType.Subvariants.Count > 0)
            {
                // Do the same check for each of the subvariants as above

                foreach (var subvariant in classType.Subvariants)
                {
                    if (subvariant.Variants != null)
                    {
                        foreach (var v in subvariant.Variants)
                        {
                            if (string.IsNullOrWhiteSpace(v))
                            {
                                classes.Add(
                                    new TailwindClass()
                                    {
                                        Name = classType.Stem + "-" + subvariant.Stem,
                                    }
                                );
                            }
                            else
                            {
                                classes.Add(
                                    new TailwindClass()
                                    {
                                        Name = classType.Stem + "-" + subvariant.Stem + "-" + v,
                                    }
                                );
                            }
                        }
                    }

                    if (subvariant.HasArbitrary == true)
                    {
                        classes.Add(
                            new TailwindClass()
                            {
                                Name = classType.Stem + "-" + subvariant.Stem + "-",
                                HasArbitrary = true,
                            }
                        );
                    }
                }
            }

            if (
                (classType.DirectVariants == null || classType.DirectVariants.Count == 0)
                && (classType.Subvariants == null || classType.Subvariants.Count == 0)
            )
            {
                var newClass = new TailwindClass() { Name = classType.Stem };
                if (classType.UseColors == true)
                {
                    newClass.UseColors = true;
                    newClass.UseOpacity = true;
                    newClass.Name = newClass.Name.Replace("{c}", "{0}");
                }
                else if (classType.UseSpacing == true)
                {
                    newClass.UseSpacing = true;
                    newClass.Name = newClass.Name.Replace("{s}", "{0}");
                }
                else if (classType.UseNumbers == true)
                {
                    newClass.UseNumbers = true;
                    newClass.Name = newClass.Name.Replace("{n}", "{0}");
                }
                else if (classType.UsePercent == true)
                {
                    newClass.UsePercent = true;
                    newClass.Name = newClass.Name.Replace("{%}", "{0}");
                }
                else if (classType.UseFractions == true)
                {
                    newClass.UseFractions = true;
                    newClass.Name = newClass.Name.Replace("{f}", "{0}");
                }
                classes.Add(newClass);
            }

            if (
                classType.HasArbitrary == true
                || classType.UseFractions == true
                || classType.UseSpacing == true
                || classType.UsePercent == true
                || classType.UseColors == true
                || classType.UseNumbers == true
            )
            {
                classes.Add(
                    new TailwindClass()
                    {
                        Name =
                            classType
                                .Stem.Replace("{c}", "")
                                .Replace("{s}", "")
                                .Replace("{n}", "")
                                .Replace("{%}", "")
                                .Replace("{f}", "")
                                .TrimEnd('-') + "-",
                        HasArbitrary = true,
                    }
                );
            }

            project.Classes.AddRange(classes);

            if (classType.HasNegative == true)
            {
                var negativeClasses = classes
                    .Select(c =>
                    {
                        return new TailwindClass()
                        {
                            Name = $"-{c.Name}",
                            UseColors = c.UseColors,
                            UseSpacing = c.UseSpacing,
                            UseNumbers = c.UseNumbers,
                            UsePercent = c.UsePercent,
                            UseFractions = c.UseFractions,
                            UseOpacity = c.UseOpacity,
                            HasArbitrary = c.HasArbitrary,
                        };
                    })
                    .ToList();

                project.Classes.AddRange(negativeClasses);
            }
        }

        foreach (
            var breakpoints in project.CssVariables.Where(v => v.Key.StartsWith("--breakpoint-"))
        )
        {
            var breakpointName = breakpoints.Key.Replace("--breakpoint-", "");
            project.Breakpoints[breakpointName] = breakpoints.Value;
        }

        foreach (
            var containers in project.CssVariables.Where(v => v.Key.StartsWith("--container-"))
        )
        {
            var breakpointName = containers.Key.Replace("--container-", "");
            project.Containers[breakpointName] = containers.Value;
        }

        var keys = project.CssVariables.Keys.OrderBy(k => k).ToList();

        HashSet<string> stems = ["--color-"];

        for (int i = 0; i < keys.Count; i++)
        {
            var key = keys[i];

            if (key.StartsWith("--default"))
            {
                stems.Add(key);
                continue;
            }

            if (i + 1 == keys.Count || key.LastIndexOf('-') == 1)
            {
                stems.Add(key);
            }

            var best = key;
            var bestMatches = 0;

            key = key.TrimStart('-');

            while (key.Contains('-'))
            {
                key = key.Substring(0, key.LastIndexOf('-'));

                var searchKey = $"--{key}-";
                var matches = keys.Count(k => k.StartsWith(searchKey));

                if (matches <= bestMatches)
                {
                    break;
                }

                bestMatches = matches;
                best = searchKey;
            }

            stems.Add(best);
            i += Math.Max(0, bestMatches - 1);
        }

        project.ThemeStems = [.. stems.OrderBy(s => s)];

        _unsetProjectCompletionConfigurations[version] = project;
    }
}
