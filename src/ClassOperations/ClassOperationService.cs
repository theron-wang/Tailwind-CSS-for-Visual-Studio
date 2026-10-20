using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
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

    [Import]
    public ITextUndoHistoryRegistry UndoHistoryRegistry { get; set; } = null!;

    public bool Processing => Volatile.Read(ref _pendingRuns) > 0;

    private TailwindSettings _settings = null!;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private Task? _initTask;
    private int _pendingRuns;
    private int _configurationVersion;
    private readonly HashSet<string> _savingDocuments = new(StringComparer.OrdinalIgnoreCase);
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
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var isOpen = await VS.Documents.IsOpenAsync(path);
        var view = await GetOpenDocumentAsync(path);
        var snapshot = view?.TextBuffer?.CurrentSnapshot;
        var wasDirty = view?.Document?.IsDirty == true;
        if (isOpen && snapshot is null)
        {
            return;
        }

        // Read open files from the editor, including unsaved edits. A snapshot
        // also lets us detect typing/undo while an asynchronous operation runs.
        await TaskScheduler.Default;
        string content;
        (byte[] bytes, string text, Encoding encoding)? disk = null;
        if (snapshot is not null)
        {
            content = snapshot.GetText();
            if (!wasDirty)
            {
                disk = await ReadFileAsync(path);
                if (disk.Value.text != content)
                {
                    await ReportSkippedFileAsync(path);
                    return;
                }
            }
        }
        else
        {
            // An open non-text document must never be overwritten via disk I/O.
            if (view is not null)
            {
                return;
            }
            disk = await ReadFileAsync(path);
            content = disk.Value.text;
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
            if (version != Volatile.Read(ref _configurationVersion))
            {
                return;
            }
            var applied = snapshot is not null
                ? await ApplyToOpenDocumentAsync(
                    path,
                    snapshot,
                    result,
                    wasDirty,
                    disk?.bytes,
                    version
                )
                : await WriteClosedFileAsync(
                    path,
                    disk!.Value.bytes,
                    result,
                    disk.Value.encoding,
                    version
                );
            if (!applied)
            {
                await ReportSkippedFileAsync(path);
                return;
            }
        }

        if (version == Volatile.Read(ref _configurationVersion))
        {
            _processed[cacheKey] = result;
        }
    }

    private static async Task<DocumentView?> GetOpenDocumentAsync(string path) =>
        await VS.Documents.IsOpenAsync(path) ? await VS.Documents.GetDocumentViewAsync(path) : null;

    private static async Task<(byte[] bytes, string text, Encoding encoding)> ReadFileAsync(
        string path
    )
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var copy = new MemoryStream();
        await file.CopyToAsync(copy);
        var bytes = copy.ToArray();
        copy.Position = 0;
        using var reader = new StreamReader(
            copy,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: true
        );
        var text = await reader.ReadToEndAsync();
        return (bytes, text, reader.CurrentEncoding);
    }

    private async Task<bool> ApplyToOpenDocumentAsync(
        string path,
        ITextSnapshot snapshot,
        string result,
        bool wasDirty,
        byte[]? originalBytes,
        int version
    )
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var view = await GetOpenDocumentAsync(path);
        if (
            view?.Document is null
            || !ReferenceEquals(view.TextBuffer?.CurrentSnapshot, snapshot)
            || !view.Document.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)
            || version != Volatile.Read(ref _configurationVersion)
            || (originalBytes is not null && !File.ReadAllBytes(path).SequenceEqual(originalBytes))
        )
        {
            return false;
        }

        using var transaction = UndoHistoryRegistry.TryGetHistory(view.TextBuffer, out var history)
            ? history.CreateTransaction("Tailwind CSS class operations")
            : null;
        using var edit = view.TextBuffer.CreateEdit(EditOptions.DefaultMinimalChange, null, null);
        if (!edit.Replace(0, snapshot.Length, result))
        {
            edit.Cancel();
            return false;
        }
        edit.Apply();
        if (edit.Canceled)
        {
            return false;
        }
        transaction?.Complete();

        // Keep pre-existing unsaved changes unsaved. Clean files and on-save
        // operations retain the existing behavior of persisting the result.
        if (!wasDirty)
        {
            _savingDocuments.Add(path);
            try
            {
                view.Document.Save();
            }
            finally
            {
                _savingDocuments.Remove(path);
            }
        }
        return true;
    }

    private async Task<bool> WriteClosedFileAsync(
        string path,
        byte[] originalBytes,
        string result,
        Encoding encoding,
        int version
    )
    {
        await TaskScheduler.Default;
        // Hold an exclusive handle across validation and writing. Opening an
        // editor or another writer cannot slip between the comparison and write.
        FileStream file;
        try
        {
            file = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
            when (ex is FileNotFoundException or DirectoryNotFoundException
                || (ex.HResult & 0xFFFF) is 32 or 33
            )
        {
            return false;
        }
        using var ownedFile = file;
        using var current = new MemoryStream();
        await file.CopyToAsync(current);
        if (!current.ToArray().SequenceEqual(originalBytes))
        {
            return false;
        }
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (
            await VS.Documents.IsOpenAsync(path)
            || version != Volatile.Read(ref _configurationVersion)
        )
        {
            return false;
        }
        await TaskScheduler.Default;
        file.Position = 0;
        using var writer = new StreamWriter(file, encoding, 1024, leaveOpen: true);
        await writer.WriteAsync(result);
        await writer.FlushAsync();
        file.SetLength(file.Position);
        return true;
    }

    private static Task ReportSkippedFileAsync(string path) =>
        VS.StatusBar.ShowMessageAsync(
            $"Tailwind CSS: Skipped '{Path.GetFileName(path)}' because its contents changed or it is unavailable for editing. Run the operation again to process the current content."
        );

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
        if (_savingDocuments.Contains(path))
        {
            return;
        }
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
