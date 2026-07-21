using System.Collections;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;

namespace Community.VisualStudio.Toolkit
{
    public static class VS
    {
        public static MessageBoxProxy MessageBox { get; } = new();

        public sealed class MessageBoxProxy
        {
            public void ShowError(string message) { }
        }
    }

    public static class ExceptionLoggingExtensions
    {
        public static void Log(this Exception ex, string message) { }

        public static Task LogAsync(this Exception ex)
        {
            return Task.CompletedTask;
        }
    }

    public static class StringExtensions
    {
        public static string TrimPrefix(
            this string value,
            string prefix,
            StringComparison comparison
        )
        {
            return value.StartsWith(prefix, comparison) ? value[prefix.Length..] : value;
        }
    }

    public sealed class PhysicalFile
    {
        public static Task<PhysicalFile?> FromFileAsync(string path)
        {
            return Task.FromResult<PhysicalFile?>(null);
        }

        public Project? ContainingProject { get; init; }
    }

    public sealed class Project
    {
        public string FullPath { get; init; } = string.Empty;
    }

    public class BaseOptionModel<T> { }

    public class BaseOptionPage<T> { }
}

namespace Microsoft.VisualStudio.Shell
{
    public static class ThreadHelper
    {
        public static JoinableTaskFactory JoinableTaskFactory { get; } = new();
    }

    public sealed class JoinableTaskFactory
    {
        public T Run<T>(Func<Task<T>> asyncMethod)
        {
            return asyncMethod().GetAwaiter().GetResult();
        }

        public Task SwitchToMainThreadAsync()
        {
            return Task.CompletedTask;
        }
    }
}

namespace Microsoft.VisualStudio.Threading { }

namespace Microsoft.VisualStudio.Text
{
    public enum SpanTrackingMode
    {
        EdgeExclusive,
        EdgeInclusive,
    }

    public readonly struct Span(int start, int length)
    {
        public int Start { get; } = start;
        public int Length { get; } = length;
        public int End => Start + Length;
        public bool IsEmpty => Length == 0;

        public bool IntersectsWith(Span other)
        {
            return Start < other.End && other.Start < End;
        }

        public bool Contains(Span other)
        {
            return Start <= other.Start && End >= other.End;
        }
    }

    public interface ITextSnapshot
    {
        int Length { get; }
        char this[int index] { get; }
        string GetText(int startIndex, int length);
    }

    public sealed class StringTextSnapshot(string text) : ITextSnapshot
    {
        private readonly string _text = text;
        public int Length => _text.Length;
        public char this[int index] => _text[index];

        public string GetText(int startIndex, int length)
        {
            return _text.Substring(startIndex, length);
        }
    }

    public interface ITextBuffer
    {
        ITextSnapshot CurrentSnapshot { get; }
        PropertyCollection Properties { get; }
    }

    public sealed class FakeTextBuffer(string text) : ITextBuffer
    {
        public ITextSnapshot CurrentSnapshot { get; } = new StringTextSnapshot(text);
        public PropertyCollection Properties { get; } = new();
    }

    public sealed class PropertyCollection : IEnumerable<KeyValuePair<object, object>>
    {
        private readonly Dictionary<object, object> _values = [];

        public T GetOrCreateSingletonProperty<T>(Func<T> creator)
        {
            if (_values.TryGetValue(typeof(T), out var existing) && existing is T typed)
            {
                return typed;
            }

            var created = creator();
            _values[typeof(T)] = created!;
            return created;
        }

        public IEnumerator<KeyValuePair<object, object>> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
    }

    public interface ITrackingSpan { }

    public readonly struct SnapshotPoint(ITextSnapshot snapshot, int position)
    {
        public ITextSnapshot Snapshot { get; } = snapshot;
        public int Position { get; } = position;

        public static SnapshotPoint operator +(SnapshotPoint point, int value)
        {
            return new SnapshotPoint(point.Snapshot, point.Position + value);
        }

        public static SnapshotPoint operator -(SnapshotPoint point, int value)
        {
            return new SnapshotPoint(point.Snapshot, point.Position - value);
        }

        public static explicit operator int(SnapshotPoint point)
        {
            return point.Position;
        }
    }

    public sealed class FakeTrackingSpan(int start, int length) : ITrackingSpan
    {
        public int Start { get; } = start;
        public int Length { get; } = length;
    }

    public readonly struct SnapshotSpan
    {
        public SnapshotSpan(ITextSnapshot snapshot, int start, int length)
        {
            Snapshot = snapshot;
            Span = new(start, length);
        }

        public SnapshotSpan(SnapshotPoint start, int length)
        {
            Snapshot = start.Snapshot;
            Span = new(start.Position, length);
        }

        public ITextSnapshot Snapshot { get; }
        public Span Span { get; }

        public SnapshotPoint Start => new(Snapshot, Span.Start);
        public SnapshotPoint End => new(Snapshot, Span.End);
        public int Length => Span.Length;

        public bool IsEmpty => Span.IsEmpty;

        public string GetText()
        {
            return Snapshot.GetText(Span.Start, Span.Length);
        }

        public bool Contains(SnapshotSpan other)
        {
            return Span.Contains(other.Span);
        }

        public bool IntersectsWith(SnapshotSpan other)
        {
            return Span.IntersectsWith(other.Span);
        }

        public override bool Equals(object? obj)
        {
            return obj is SnapshotSpan other
                && ReferenceEquals(Snapshot, other.Snapshot)
                && Span.Start == other.Span.Start
                && Span.Length == other.Span.Length;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Snapshot, Span.Start, Span.Length);
        }
    }

    public static class TextSnapshotExtensions
    {
        public static ITrackingSpan CreateTrackingSpan(
            this ITextSnapshot snapshot,
            int start,
            int length,
            SpanTrackingMode mode
        )
        {
            return new FakeTrackingSpan(start, length);
        }

        public static ITrackingSpan CreateTrackingSpan(
            this ITextSnapshot snapshot,
            SnapshotSpan span,
            SpanTrackingMode mode
        )
        {
            return new FakeTrackingSpan((int)span.Start, span.Length);
        }
    }
}

namespace TailwindCSSIntellisense.Completions
{
    public sealed class ProjectConfigurationManager
    {
        private readonly Dictionary<string, ProjectCompletionValues> _byFilePath = new(
            StringComparer.InvariantCultureIgnoreCase
        );

        public ProjectCompletionValues? DefaultProject { get; private set; }

        internal ConfigurationState Configuration { get; } = new();

        public void Seed(string filePath, ProjectCompletionValues values)
        {
            _byFilePath[filePath] = values;
            DefaultProject ??= values;
        }

        public void SeedDefault(ProjectCompletionValues values)
        {
            DefaultProject = values;
        }

        public Task<ProjectCompletionValues> GetCompletionConfigurationByFilePathAsync(
            string? filePath
        )
        {
            if (
                !string.IsNullOrWhiteSpace(filePath)
                && _byFilePath.TryGetValue(filePath, out var value)
            )
            {
                return Task.FromResult(value);
            }

            if (DefaultProject is not null)
            {
                return Task.FromResult(DefaultProject);
            }

            return Task.FromResult(new ProjectCompletionValues { Version = TailwindVersion.V3 });
        }

        internal sealed class ConfigurationState
        {
            internal TailwindConfiguration? LastConfig { get; set; }
        }
    }
}

namespace TailwindCSSIntellisense.ClassSort
{
    public sealed class ClassSortUtilities
    {
        private readonly Dictionary<TailwindVersion, Dictionary<string, int>> _classOrders;
        private readonly Dictionary<TailwindVersion, Dictionary<string, int>> _variantOrders;

        public ClassSortUtilities(
            Dictionary<TailwindVersion, Dictionary<string, int>> classOrders,
            Dictionary<TailwindVersion, Dictionary<string, int>> variantOrders
        )
        {
            _classOrders = classOrders;
            _variantOrders = variantOrders;
        }

        public async Task<Dictionary<string, int>> GetClassOrderAsync(TailwindVersion version)
        {
            if (_classOrders.TryGetValue(version, out var classOrder))
            {
                return classOrder;
            }

            return _classOrders.Values.FirstOrDefault() ?? [];
        }

        public async Task<Dictionary<string, int>> GetVariantOrderAsync(TailwindVersion version)
        {
            if (_variantOrders.TryGetValue(version, out var variantOrder))
            {
                return variantOrder;
            }

            return _variantOrders.Values.FirstOrDefault() ?? [];
        }
    }
}

namespace TailwindCSSIntellisense.Linting
{
    internal sealed class LinterUtilities
    {
        public static Func<ErrorType, ErrorSeverity> GetErrorSeverityHandler { get; set; } = _ =>
            ErrorSeverity.Warning;

        public static Func<
            IEnumerable<string>,
            ProjectCompletionValues,
            IEnumerable<(string className, string errorMessage, IEnumerable<string> conflictingClasses)>
        > CheckForClassDuplicatesHandler { get; set; } = (_, _) => [];

        public ErrorSeverity GetErrorSeverity(ErrorType type) => GetErrorSeverityHandler(type);

        public IEnumerable<(string className, string errorMessage, IEnumerable<string> conflictingClasses)> CheckForClassDuplicates(
            IEnumerable<string> classes,
            ProjectCompletionValues projectCompletionValues
        )
        {
            return CheckForClassDuplicatesHandler(classes, projectCompletionValues);
        }
    }
}

namespace TailwindCSSIntellisense.Parsers
{
    using Microsoft.VisualStudio.Text;

    internal static class CssParser
    {
        public static IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span)
        {
            return [span];
        }
    }
}

namespace TailwindCSSIntellisense.Linting.Validators
{
    using Microsoft.VisualStudio.Text;
    using TailwindCSSIntellisense.Linting.Validators.Diagnostics;

    internal abstract class Validator
    {
        protected readonly ITextBuffer _buffer;
        protected readonly LinterUtilities _linterUtils;
        protected readonly ProjectConfigurationManager _projectConfigurationManager;
        protected readonly CompletionConfiguration _completionConfiguration;
        protected readonly DiagnosticsAggregator _diagnosticsAggregator;
        protected ProjectCompletionValues _projectCompletionValues;
        protected readonly HashSet<SnapshotSpan> _checkedSpans = [];

        protected Validator(
            ITextBuffer buffer,
            LinterUtilities linterUtils,
            ProjectConfigurationManager projectConfigurationManager,
            CompletionConfiguration completionConfiguration,
            DiagnosticsAggregator diagnosticsAggregator
        )
        {
            _buffer = buffer;
            _linterUtils = linterUtils;
            _projectConfigurationManager = projectConfigurationManager;
            _completionConfiguration = completionConfiguration;
            _diagnosticsAggregator = diagnosticsAggregator;
            _projectCompletionValues = projectConfigurationManager
                .GetCompletionConfigurationByFilePathAsync(null)
                .Result;
        }

        protected bool IsAlreadyChecked(SnapshotSpan scope) => false;

        protected abstract IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span);
        protected abstract IEnumerable<Error> ComputeErrors(SnapshotSpan span);
    }
}

namespace TailwindCSSIntellisense.ClassSort.Sorters
{
    internal static class SorterStringExtensions
    {
        public static string TrimPrefix(
            this string value,
            string prefix,
            StringComparison comparison
        )
        {
            return value.StartsWith(prefix, comparison) ? value[prefix.Length..] : value;
        }
    }

    internal sealed class CssSorter : Sorter
    {
        public override string[] Handled { get; } = [".css"];

        protected override async IAsyncEnumerable<string> GetSegmentsAsync(
            string filePath,
            string input
        )
        {
            yield return input;
        }
    }
}

namespace TailwindCSSIntellisense.Settings
{
    public sealed class TailwindSettings
    {
        public bool UseCli { get; set; }
        public string? TailwindCliPath { get; set; }
        public CustomRegexes CustomRegexes { get; set; } = new();
    }

    public sealed class CustomRegexes
    {
        public CustomRegex Razor { get; set; } = new();
        public CustomRegex HTML { get; set; } = new();
        public CustomRegex JavaScript { get; set; } = new();

        public class CustomRegex
        {
            public bool Override { get; set; } = false;
            public List<string> Values { get; set; } = [];
        }
    }
}

namespace TailwindCSSIntellisense.Configuration
{
    public class CompletionConfiguration { }
}
