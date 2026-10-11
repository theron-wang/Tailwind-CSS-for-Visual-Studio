using System;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Differencing;
using Microsoft.VisualStudio.Threading;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.ClassOperations;

/// <summary>
/// Owns the save/build subscriptions and file I/O for the ordered operation pipeline.
/// All entry points share a gate so operations cannot overwrite concurrent changes by one another.
/// </summary>
[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class ClassOperationService : IDisposable
{
    [Import]
    public SettingsProvider SettingsProvider { get; set; } = null!;

    [Import]
    public ClassOperationAggregator Operations { get; set; } = null!;

    [Import]
    public CompletionConfiguration CompletionConfiguration { get; set; } = null!;

    [Import]
    public FileFinder FileFinder { get; set; } = null!;

    public bool Processing => Volatile.Read(ref _pendingRuns) > 0;

    private TailwindSettings _settings = null!;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private Task? _initTask;
    private int _pendingRuns;
    private int _configurationVersion;
    private readonly ConcurrentDictionary<string, string> _processed = new(
        StringComparer.OrdinalIgnoreCase
    );

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
        _settings = await SettingsProvider.GetSettingsAsync();
        VS.Events.DocumentEvents.Saved += DocumentSaved;
        VS.Events.BuildEvents.SolutionBuildStarted += OnBuild;
        SettingsProvider.OnSettingsChanged += OnSettingsChangedAsync;
        CompletionConfiguration.ConfigurationUpdated += ConfigurationChangedAsync;
    }

    public async Task ProcessAllAsync(
        SortClassesOptions? trigger = null,
        Type? operationType = null
    )
    {
        await TaskScheduler.Default;
        await InitializeAsync();
        Interlocked.Increment(ref _pendingRuns);
        await _operationLock.WaitAsync();
        try
        {
            var extensions = Operations
                .GetEnabledOperations(_settings, trigger, operationType)
                .SelectMany(o => o.AllHandled)
                .Distinct()
                .ToArray();
            if (extensions.Length == 0)
            {
                return;
            }
            var files = await FileFinder.TraverseAllProjectsAndFindFilesOfTypeAsync(extensions);
            for (int i = 0; i < files.Count; i++)
            {
                await ProcessFileAsync(files[i], trigger, trigger is null, operationType);
                await VS.StatusBar.ShowProgressAsync(
                    $"Tailwind CSS: Processing classes ({i + 1}/{files.Count} files done)",
                    i + 1,
                    files.Count
                );
            }
            await VS.StatusBar.ShowProgressAsync("Tailwind CSS: Class operations complete", 1, 1);
            await VS.StatusBar.ShowMessageAsync("Tailwind CSS: Class operations complete");
        }
        catch (Exception ex)
        {
            await VS.StatusBar.ShowProgressAsync(ErrorMessage, 1, 1);
            await ReportErrorAsync(ex);
        }
        finally
        {
            _operationLock.Release();
            Interlocked.Decrement(ref _pendingRuns);
        }
    }

    public async Task ProcessAsync(
        string path,
        SortClassesOptions? trigger = null,
        Type? operationType = null
    )
    {
        await TaskScheduler.Default;
        await InitializeAsync();
        Interlocked.Increment(ref _pendingRuns);
        await _operationLock.WaitAsync();
        try
        {
            await ProcessFileAsync(path, trigger, true, operationType);
        }
        finally
        {
            _operationLock.Release();
            Interlocked.Decrement(ref _pendingRuns);
        }
    }

    private async Task ProcessFileAsync(
        string path,
        SortClassesOptions? trigger,
        bool force,
        Type? operationType
    )
    {
        if (
            !Operations
                .GetEnabledOperations(_settings, trigger, operationType)
                .Any(o => o.Handled(path))
        )
        {
            return;
        }

        var version = Volatile.Read(ref _configurationVersion);
        string content;
        System.Text.Encoding encoding;
        using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(file))
        {
            content = await reader.ReadToEndAsync();
            // Read first so BOM detection has completed.
            encoding = reader.CurrentEncoding;
        }

        var cacheKey = path + "|" + trigger + "|" + operationType;
        if (!force && _processed.TryGetValue(cacheKey, out var previous) && previous == content)
        {
            return;
        }

        var result = await Operations.ProcessAsync(
            path,
            content,
            _settings,
            trigger,
            operationType
        );
        if (result != content)
        {
            using (
                var file = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)
            )
            using (var writer = new StreamWriter(file, encoding))
            {
                await writer.WriteAsync(result);
            }

            if (await VS.Documents.IsOpenAsync(path))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
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

        if (version == Volatile.Read(ref _configurationVersion))
        {
            _processed[cacheKey] = result;
        }
    }

    private const string ErrorMessage =
        "Tailwind CSS: Error occurred while processing classes (check Extensions output pane for more details)";

    private static async Task ReportErrorAsync(Exception ex)
    {
        await VS.StatusBar.ShowMessageAsync(ErrorMessage);
        await ex.LogAsync();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "RunAsync with FileAndForget is ok"
    )]
    private void DocumentSaved(string path)
    {
        if (Operations.GetEnabledOperations(_settings, SortClassesOptions.OnSave).Any())
        {
            ThreadHelper
                .JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await ProcessAsync(path, SortClassesOptions.OnSave);
                    }
                    catch (Exception ex)
                    {
                        await ReportErrorAsync(ex);
                    }
                })
                .FileAndForget(nameof(TailwindCSSIntellisense) + "/ClassOperations/OnDocumentSave");
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "RunAsync with FileAndForget is ok"
    )]
    private void OnBuild(object sender, EventArgs e)
    {
        if (Operations.GetEnabledOperations(_settings, SortClassesOptions.OnBuild).Any())
        {
            ThreadHelper
                .JoinableTaskFactory.RunAsync(() => ProcessAllAsync(SortClassesOptions.OnBuild))
                .FileAndForget(nameof(TailwindCSSIntellisense) + "/ClassOperations/OnBuild");
        }
    }

    private Task ConfigurationChangedAsync()
    {
        Interlocked.Increment(ref _configurationVersion);
        _processed.Clear();
        return Task.CompletedTask;
    }

    private Task OnSettingsChangedAsync(TailwindSettings settings)
    {
        _settings = settings;
        return ConfigurationChangedAsync();
    }

    public void Dispose()
    {
        VS.Events.DocumentEvents.Saved -= DocumentSaved;
        VS.Events.BuildEvents.SolutionBuildStarted -= OnBuild;
        SettingsProvider.OnSettingsChanged -= OnSettingsChangedAsync;
        CompletionConfiguration.ConfigurationUpdated -= ConfigurationChangedAsync;
    }
}
