using System.Text;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using TailwindCSSIntellisense.ClassOperations;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

public class ClassOperationServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(SortClassesOptions.OnSave)]
    [InlineData(SortClassesOptions.OnBuild)]
    public async Task OpenCleanFile_UsesOneUndoableEditAndSavesWithoutRecursing(
        SortClassesOptions? trigger
    )
    {
        using var fixture = new Fixture();
        var view = fixture.Open("unsorted");
        fixture.Settings.SortClassesType = trigger ?? SortClassesOptions.Manual;

        await fixture.Service.ProcessAsync(fixture.Path, trigger);

        Assert.Equal("sorted", view.TextBuffer!.CurrentSnapshot.GetText());
        Assert.Equal("sorted", await File.ReadAllTextAsync(fixture.Path));
        Assert.Equal(1, view.Document!.Saves);
        Assert.Equal(1, fixture.Operation.Calls);
        Assert.False(view.Document.IsDirty);
        fixture.Undo.TryGetHistory(view.TextBuffer, out var history);
        history.Undo();
        Assert.Equal("unsorted", view.TextBuffer.CurrentSnapshot.GetText());
        Assert.False(fixture.Service.Processing);
    }

    [Fact]
    public async Task OpenDirtyFile_ProcessesUnsavedContentAndKeepsItUnsaved()
    {
        using var fixture = new Fixture();
        var view = fixture.Open("unsaved classes");
        fixture.Operation.Transform = text => text + " sorted";

        await fixture.Service.ProcessAsync(fixture.Path);

        Assert.Equal("unsaved classes sorted", view.TextBuffer!.CurrentSnapshot.GetText());
        Assert.Equal("unsorted", await File.ReadAllTextAsync(fixture.Path));
        Assert.True(view.Document!.IsDirty);
        Assert.Equal(0, view.Document.Saves);
        fixture.Undo.TryGetHistory(view.TextBuffer, out var history);
        history.Undo();
        Assert.Equal("unsaved classes", view.TextBuffer.CurrentSnapshot.GetText());
    }

    public enum ConcurrentChange
    {
        Typing,
        TypingThenUndo,
        SaveNewEdits,
        Close,
        Reopen,
        Rename,
        DiskWrite,
        Settings,
    }

    [Theory]
    [InlineData(ConcurrentChange.Typing)]
    [InlineData(ConcurrentChange.TypingThenUndo)]
    [InlineData(ConcurrentChange.SaveNewEdits)]
    [InlineData(ConcurrentChange.Close)]
    [InlineData(ConcurrentChange.Reopen)]
    [InlineData(ConcurrentChange.Rename)]
    [InlineData(ConcurrentChange.DiskWrite)]
    [InlineData(ConcurrentChange.Settings)]
    public async Task OpenFile_ConcurrentChangeDiscardsStaleResult(ConcurrentChange change)
    {
        using var fixture = new Fixture();
        var view = fixture.Open("unsorted");
        fixture.Operation.Pause();
        var run = fixture.Service.ProcessAsync(fixture.Path);
        await fixture.Operation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fixture.Service.Processing);

        switch (change)
        {
            case ConcurrentChange.Typing:
            case ConcurrentChange.TypingThenUndo:
            case ConcurrentChange.SaveNewEdits:
                var text = change == ConcurrentChange.TypingThenUndo ? "unsorted" : "new edits";
                ((FakeTextBuffer)view.TextBuffer!).CurrentSnapshot = new StringTextSnapshot(
                    text,
                    view.TextBuffer
                );
                if (change == ConcurrentChange.SaveNewEdits)
                {
                    view.Document!.Save();
                }
                break;
            case ConcurrentChange.Close:
                VS.Documents.Open.TryRemove(fixture.Path, out _);
                break;
            case ConcurrentChange.Reopen:
                fixture.Open("new edits");
                break;
            case ConcurrentChange.Rename:
                view.Document!.FilePath = fixture.Path + ".renamed";
                break;
            case ConcurrentChange.DiskWrite:
                await File.WriteAllTextAsync(fixture.Path, "external edits");
                break;
            case ConcurrentChange.Settings:
                await fixture.Service.SettingsProvider.OnSettingsChanged!(fixture.Settings);
                break;
        }
        fixture.Operation.Resume();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            change is ConcurrentChange.Typing or ConcurrentChange.SaveNewEdits
                ? "new edits"
                : "unsorted",
            view.TextBuffer!.CurrentSnapshot.GetText()
        );
        var expectedDisk = change switch
        {
            ConcurrentChange.SaveNewEdits => "new edits",
            ConcurrentChange.DiskWrite => "external edits",
            _ => "unsorted",
        };
        Assert.Equal(expectedDisk, await File.ReadAllTextAsync(fixture.Path));
        Assert.Equal(0, ((FakeTextBuffer)view.TextBuffer).AppliedEdits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedFile_ConcurrentWriteOrOpenIsPreservedAndCanBeRetried(bool open)
    {
        using var fixture = new Fixture();
        fixture.Settings.SortClassesType = SortClassesOptions.OnBuild;
        fixture.Operation.Pause();
        var run = fixture.Service.ProcessAllAsync(SortClassesOptions.OnBuild);
        await fixture.Operation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (open)
        {
            fixture.Open("new unsaved edits");
        }
        else
        {
            await File.WriteAllTextAsync(fixture.Path, "external edits");
        }
        fixture.Operation.Resume();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            open ? "unsorted" : "external edits",
            await File.ReadAllTextAsync(fixture.Path)
        );
        if (open)
        {
            Assert.Equal(
                "new unsaved edits",
                VS.Documents.Open[fixture.Path].TextBuffer!.CurrentSnapshot.GetText()
            );
        }

        await fixture.Service.ProcessAllAsync(SortClassesOptions.OnBuild);
        Assert.Equal(2, fixture.Operation.Calls);
        Assert.Equal(
            "sorted",
            open
                ? VS.Documents.Open[fixture.Path].TextBuffer!.CurrentSnapshot.GetText()
                : await File.ReadAllTextAsync(fixture.Path)
        );
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16")]
    [InlineData("utf16-be")]
    public async Task ClosedFile_PreservesEncodingAndBomAndTruncatesShorterOutput(
        string encodingName
    )
    {
        using var fixture = new Fixture();
        var encoding = encodingName switch
        {
            "utf8-bom" => new UTF8Encoding(true),
            "utf16" => Encoding.Unicode,
            "utf16-be" => Encoding.BigEndianUnicode,
            _ => (Encoding)new UTF8Encoding(false),
        };
        await File.WriteAllTextAsync(fixture.Path, "unsorted 日本語", encoding);
        fixture.Operation.Transform = _ => "日本語";

        await fixture.Service.ProcessAsync(fixture.Path);

        Assert.Equal(
            encoding.GetPreamble().Concat(encoding.GetBytes("日本語")),
            await File.ReadAllBytesAsync(fixture.Path)
        );
    }

    [Fact]
    public async Task OpenReadOnlyFile_DoesNotSaveOrApplyChanges()
    {
        using var fixture = new Fixture();
        var view = fixture.Open("unsorted");
        ((FakeTextBuffer)view.TextBuffer!).RejectEdits = true;

        await fixture.Service.ProcessAsync(fixture.Path);

        Assert.Equal("unsorted", view.TextBuffer.CurrentSnapshot.GetText());
        Assert.Equal(0, view.Document!.Saves);
        Assert.Equal("unsorted", await File.ReadAllTextAsync(fixture.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedFile_DeletedOrLockedDuringProcessingIsNotRecreatedOrOverwritten(
        bool locked
    )
    {
        using var fixture = new Fixture();
        fixture.Operation.Pause();
        var run = fixture.Service.ProcessAsync(fixture.Path);
        await fixture.Operation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var blockingFile = locked
            ? File.Open(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            : null;
        if (!locked)
        {
            File.Delete(fixture.Path);
        }
        fixture.Operation.Resume();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        blockingFile?.Dispose();

        Assert.Equal(locked, File.Exists(fixture.Path));
        if (locked)
        {
            Assert.Equal("unsorted", await File.ReadAllTextAsync(fixture.Path));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"tailwind-operations-{Guid.NewGuid():N}.html"
            );
        public ControlledOperation Operation { get; } = new();
        public FakeUndoHistoryRegistry Undo { get; } = new();
        public TailwindSettings Settings { get; } =
            new()
            {
                EnableTailwindCss = true,
                ConfigurationFiles = [new() { Path = "tailwind.css" }],
                SortClassesType = SortClassesOptions.Manual,
            };
        public ClassOperationService Service { get; }

        public Fixture()
        {
            File.WriteAllText(Path, "unsorted", new UTF8Encoding(false));
            var finder = new FileFinder();
            finder.JavascriptFiles.Add(Path);
            Service = new()
            {
                SettingsProvider = new SettingsProvider { Settings = Settings },
                Operations = new([Operation]),
                CompletionConfiguration = new(),
                FileFinder = finder,
                UndoHistoryRegistry = Undo,
            };
        }

        public DocumentView Open(string text)
        {
            var buffer = new FakeTextBuffer(text, Path);
            var view = new DocumentView { TextBuffer = buffer, Document = new(buffer, Path) };
            VS.Documents.Open[Path] = view;
            return view;
        }

        public void Dispose()
        {
            Operation.Resume();
            Service.Dispose();
            VS.Documents.Open.TryRemove(Path, out _);
            File.Delete(Path);
        }
    }

    private sealed class ControlledOperation : ClassOperation
    {
        private TaskCompletionSource? _resume;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<string, string> Transform { get; set; } = _ => "sorted";
        public int Calls { get; private set; }
        public override int Priority => 0;
        public override IEnumerable<string> AllHandled => [".html"];

        protected override SortClassesOptions GetMode(TailwindSettings settings) =>
            settings.SortClassesType;

        public void Pause() => _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Resume() => _resume?.TrySetResult();

        public override async Task<string> ProcessAsync(string path, string content)
        {
            Calls++;
            Started.TrySetResult();
            if (_resume is not null)
            {
                await _resume.Task;
            }
            return Transform(content);
        }
    }
}
