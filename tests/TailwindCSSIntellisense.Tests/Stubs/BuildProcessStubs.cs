namespace Community.VisualStudio.Toolkit
{
    public static partial class VS
    {
        public static WindowsProxy Windows { get; } = new();
        public static Func<string, Task>? WriteOutput { get; set; }

        public sealed class WindowsProxy
        {
            public Task<OutputWindowPane?> GetOutputWindowPaneAsync(object id) =>
                Task.FromResult<OutputWindowPane?>(new());

            public Task<OutputWindowPane> CreateOutputWindowPaneAsync(string name) =>
                Task.FromResult(new OutputWindowPane());
        }

        public sealed class OutputWindowPane
        {
            public Guid Guid { get; } = Guid.NewGuid();

            public Task ActivateAsync() => Task.CompletedTask;

            public Task WriteLineAsync(string text) =>
                WriteOutput?.Invoke(text) ?? Task.CompletedTask;
        }
    }
}

namespace Community.VisualStudio.Toolkit.Windows
{
    public enum VSOutputWindowPane
    {
        Build,
    }
}

namespace TailwindCSSIntellisense.Options
{
    public enum BuildProcessOptions
    {
        Default,
        Manual,
        OnBuild,
        OnSave,
        ManualJIT = 500,
        None = 1000,
    }

    public sealed class General
    {
        public bool VerboseBuild { get; set; }
        public static event Action<General>? Saved;

        public static Task<General> GetLiveInstanceAsync() => Task.FromResult(new General());

        public static void Save(General value) => Saved?.Invoke(value);
    }
}
