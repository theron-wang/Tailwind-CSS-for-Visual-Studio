using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Differencing;
using TailwindCSSIntellisense.ClassSort.Sorters;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.ClassSort;

[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class ClassSorter : IDisposable
{
    [Import]
    public SettingsProvider SettingsProvider { get; set; } = null!;

    [Import(typeof(SorterAggregator))]
    public SorterAggregator Sorter { get; set; } = null!;

    [Import]
    public CompletionConfiguration CompletionConfiguration { get; set; } = null!;

    [Import]
    public FileFinder FileFinder { get; set; } = null!;

    public bool Sorting { get; private set; }

    private TailwindSettings _tailwindSettings = null!;
    private SemaphoreSlim _initLock = new(1, 1);
    private Task? _initTask;

    private readonly HashSet<string> _sorted = [];

    public async Task InitializeAsync()
    {
        Task task;

        await _initLock.WaitAsync();
        try
        {
            _initTask ??= InitializeImplAsync();
            task = _initTask;
        }
        finally
        {
            _initLock.Release();
        }

        await task;
    }

    private async Task InitializeImplAsync()
    {
        _tailwindSettings = await SettingsProvider.GetSettingsAsync();

        VS.Events.DocumentEvents.Saved += DocumentSaved;
        VS.Events.BuildEvents.SolutionBuildStarted += OnBuild;
        SettingsProvider.OnSettingsChanged += OnSettingsChangedAsync;
        CompletionConfiguration.ConfigurationUpdated += ConfigurationChangedAsync;
    }

    public async Task SortAllAsync()
    {
        await InitializeAsync();
        Sorting = true;
        try
        {
            var files = await FileFinder.TraverseAllProjectsAndFindFilesOfTypeAsync(
                Sorter.AllHandled
            );

            for (int i = 0; i < files.Count; i++)
            {
                await SortAsync(files[i], false);
                await VS.StatusBar.ShowProgressAsync(
                    $"Tailwind CSS: Sorting classes ({i + 1}/{files.Count} files done)",
                    i + 1,
                    files.Count
                );
            }

            await VS.StatusBar.ShowProgressAsync("Tailwind CSS: Sort complete", 1, 1);
            await VS.StatusBar.ShowMessageAsync("Tailwind CSS: Sort complete");
        }
        catch (Exception ex)
        {
            await VS.StatusBar.ShowProgressAsync(
                "Tailwind CSS: Error occurred while sorting classes (check Extensions output pane for more details)",
                1,
                1
            );
            await VS.StatusBar.ShowMessageAsync(
                "Tailwind CSS: Error occurred while sorting classes (check Extensions output pane for more details)"
            );
            await ex.LogAsync();
        }
        finally
        {
            Sorting = false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "RunAsync with FileAndForget is ok"
    )]
    private void DocumentSaved(string path)
    {
        if (_sorted.Contains(path.ToLower()))
        {
            _sorted.Remove(path);
        }
        if (
            _tailwindSettings.EnableTailwindCss
            && _tailwindSettings.SortClassesType == SortClassesOptions.OnSave
        )
        {
            Sorting = true;
            ThreadHelper
                .JoinableTaskFactory.RunAsync(
                    async delegate
                    {
                        try
                        {
                            await SortAsync(path, true);
                        }
                        catch (Exception ex)
                        {
                            await VS.StatusBar.ShowMessageAsync(
                                "Tailwind CSS: Error occurred while sorting classes (check Extensions output pane for more details)"
                            );
                            await ex.LogAsync();
                        }
                        finally
                        {
                            Sorting = false;
                        }
                    }
                )
                .FileAndForget(nameof(TailwindCSSIntellisense) + "/ClassSorter/OnDocumentSave");
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "RunAsync with FileAndForget is ok"
    )]
    private void OnBuild(object sender, EventArgs e)
    {
        if (
            _tailwindSettings.EnableTailwindCss
            && _tailwindSettings.SortClassesType == SortClassesOptions.OnBuild
        )
        {
            ThreadHelper
                .JoinableTaskFactory.RunAsync(SortAllAsync)
                .FileAndForget(nameof(TailwindCSSIntellisense) + "/ClassSorter/OnBuild");
        }
    }

    public async Task SortAsync(string path, bool forceSort)
    {
        if (!Sorter.Handled(path))
        {
            return;
        }

        if (!forceSort && _sorted.Contains(path))
        {
            return;
        }

        await InitializeAsync();

        if (
            _tailwindSettings.EnableTailwindCss
            && _tailwindSettings.SortClassesType != SortClassesOptions.None
        )
        {
            string fileContent;
            Encoding encoding;

            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using var reader = new StreamReader(file);
                encoding = reader.CurrentEncoding;
                fileContent = await reader.ReadToEndAsync();
            }

            var sorted = await Sorter.SortAsync(path, fileContent);

            if (sorted != fileContent)
            {
                using (
                    var file = File.Open(
                        path,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.ReadWrite
                    )
                )
                {
                    using var writer = new StreamWriter(file, encoding);
                    await writer.WriteAsync(sorted);
                }

                // Reload - sometimes changes do not show up
                if (await VS.Documents.IsOpenAsync(path))
                {
                    var view = await VS.Documents.GetDocumentViewAsync(path);

                    view?.Document?.Reload(
                        new EditOptions(
                            new()
                            {
                                DifferenceType =
                                    StringDifferenceTypes.Line | StringDifferenceTypes.Word,
                                IgnoreTrimWhiteSpace = true,
                            }
                        )
                    );
                }
            }
            _sorted.Add(path.ToLower());
        }
    }

    private Task ConfigurationChangedAsync()
    {
        _sorted.Clear();
        return Task.CompletedTask;
    }

    private Task OnSettingsChangedAsync(TailwindSettings settings)
    {
        _tailwindSettings = settings;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        VS.Events.DocumentEvents.Saved -= DocumentSaved;
        VS.Events.BuildEvents.SolutionBuildStarted -= OnBuild;
        SettingsProvider.OnSettingsChanged -= OnSettingsChangedAsync;
        CompletionConfiguration.ConfigurationUpdated -= ConfigurationChangedAsync;
    }
}
