using System.Collections.Concurrent;
using System.Text;
using Microsoft.VisualStudio.Text;

namespace Community.VisualStudio.Toolkit
{
    public static partial class VS
    {
        public static DocumentsProxy Documents { get; } = new();
        public static StatusBarProxy StatusBar { get; } = new();

        public sealed partial class EventsProxy
        {
            public DocumentEventsProxy DocumentEvents { get; } = new();
            public BuildEventsProxy BuildEvents { get; } = new();
        }

        public sealed class DocumentEventsProxy
        {
            public event Action<string>? Saved;

            public void Save(string path) => Saved?.Invoke(path);
        }

        public sealed class BuildEventsProxy
        {
            public event Action<Project?>? ProjectBuildStarted;

            public void StartProjectBuild(Project? project) => ProjectBuildStarted?.Invoke(project);

            public event Action<object, EventArgs>? SolutionBuildStarted;

            public void StartBuild() => SolutionBuildStarted?.Invoke(this, EventArgs.Empty);
        }

        public sealed class DocumentsProxy
        {
            public ConcurrentDictionary<string, DocumentView> Open { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public Task<bool> IsOpenAsync(string path) => Task.FromResult(Open.ContainsKey(path));

            public Task<DocumentView?> GetDocumentViewAsync(string path) =>
                Task.FromResult(Open.TryGetValue(path, out var view) ? view : null);
        }

        public sealed class StatusBarProxy
        {
            public Task ShowMessageAsync(string message) => Task.CompletedTask;

            public Task ShowProgressAsync(string message, int current, int total) =>
                Task.CompletedTask;
        }
    }

    public sealed class DocumentView
    {
        public ITextBuffer? TextBuffer { get; init; }
        public FakeTextDocument? Document { get; init; }
    }

    public sealed class FakeTextDocument(
        FakeTextBuffer buffer,
        string path,
        Encoding? encoding = null
    )
    {
        private string _savedText = File.ReadAllText(path, encoding ?? new UTF8Encoding(false));
        public string FilePath { get; set; } = path;
        public bool IsDirty => buffer.CurrentSnapshot.GetText() != _savedText;
        public int Saves { get; private set; }
        public Encoding Encoding => encoding ?? new UTF8Encoding(false);

        public void Save()
        {
            var text = buffer.CurrentSnapshot.GetText();
            File.WriteAllText(FilePath, text, encoding ?? new UTF8Encoding(false));
            _savedText = text;
            Saves++;
            VS.Events.DocumentEvents.Save(FilePath);
        }
    }
}

namespace Microsoft.VisualStudio.Text
{
    public readonly struct EditOptions
    {
        public static EditOptions DefaultMinimalChange => default;
    }

    public interface ITextEdit : IDisposable
    {
        bool Canceled { get; }
        bool Replace(int start, int length, string text);
        ITextSnapshot Apply();
        void Cancel();
    }

    public sealed partial class FakeTextBuffer
    {
        public bool RejectEdits { get; set; }
        public int AppliedEdits { get; private set; }

        public ITextEdit CreateEdit(
            EditOptions options,
            int? reiteratedVersionNumber,
            object? editTag
        ) => new FakeTextEdit(this);

        private sealed class FakeTextEdit(FakeTextBuffer buffer) : ITextEdit
        {
            private string? _text;
            public bool Canceled { get; private set; }

            public bool Replace(int start, int length, string text)
            {
                if (buffer.RejectEdits)
                {
                    return false;
                }
                _text = buffer.CurrentSnapshot.GetText().Remove(start, length).Insert(start, text);
                return true;
            }

            public ITextSnapshot Apply()
            {
                if (_text is not null)
                {
                    buffer.CurrentSnapshot = new StringTextSnapshot(_text, buffer);
                    buffer.AppliedEdits++;
                }
                else
                {
                    Canceled = true;
                }
                return buffer.CurrentSnapshot;
            }

            public void Dispose() { }

            public void Cancel() => Canceled = true;
        }
    }

    public static class SnapshotTextExtensions
    {
        public static string GetText(this ITextSnapshot snapshot) =>
            snapshot.GetText(0, snapshot.Length);
    }
}

namespace Microsoft.VisualStudio.Text.Operations
{
    public interface ITextUndoHistoryRegistry
    {
        bool TryGetHistory(object context, out FakeUndoHistory history);
    }

    public sealed class FakeUndoHistoryRegistry : ITextUndoHistoryRegistry
    {
        private readonly Dictionary<object, FakeUndoHistory> _histories = [];

        public bool TryGetHistory(object context, out FakeUndoHistory history)
        {
            if (!_histories.TryGetValue(context, out history!))
            {
                history = new FakeUndoHistory((FakeTextBuffer)context);
                _histories[context] = history;
            }
            return true;
        }
    }

    public sealed class FakeUndoHistory(FakeTextBuffer buffer)
    {
        private readonly Stack<string> _undo = new();

        public FakeUndoTransaction CreateTransaction(string name) =>
            new(this, buffer.CurrentSnapshot.GetText());

        public void Undo() => buffer.CurrentSnapshot = new StringTextSnapshot(_undo.Pop(), buffer);

        public sealed class FakeUndoTransaction(FakeUndoHistory history, string before)
            : IDisposable
        {
            public void Complete() => history._undo.Push(before);

            public void Dispose() { }
        }
    }
}
