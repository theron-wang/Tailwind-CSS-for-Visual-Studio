using System.Reflection;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Linting;
using TailwindCSSIntellisense.Linting.Validators.Diagnostics;

namespace TailwindCSSIntellisense.Tests.UnitTests;

[Collection("Non-Parallel Tests")]
public class DiagnosticsTests : IDisposable
{
    private readonly Func<
        IEnumerable<string>,
        ProjectCompletionValues,
        IEnumerable<(string className, string errorMessage, IEnumerable<string> conflictingClasses)>
    > _originalConflictHandler = LinterUtilities.CheckForClassDuplicatesHandler;

    private readonly Func<ErrorType, ErrorSeverity> _originalSeverityHandler =
        LinterUtilities.GetErrorSeverityHandler;

    public void Dispose()
    {
        LinterUtilities.CheckForClassDuplicatesHandler = _originalConflictHandler;
        LinterUtilities.GetErrorSeverityHandler = _originalSeverityHandler;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DeprecatedAtRuleDiagnostics_FlagsVariantAtRule()
    {
        var errors = await GetErrorsAsync(
            new DeprecatedAtRuleDiagnostics(),
            "@variant dark (&:is(.dark *));",
            new ProjectCompletionValues { Version = TailwindVersion.V4 }
        );

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.DeprecatedAtRule, error.ErrorType);
        Assert.Contains("@custom-variant", error.ErrorMessage);
        Assert.NotNull(error.Suggestion);
    }

    [Fact]
    public async Task InvalidScreenDiagnostics_FlagsUnknownScreen()
    {
        var values = new ProjectCompletionValues { Version = TailwindVersion.V4 };
        values.Breakpoints["md"] = "768px";

        var errors = await GetErrorsAsync(new InvalidScreenDiagnostics(), "@screen lg { }", values);

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.InvalidScreen, error.ErrorType);
        Assert.Contains("does not exist", error.ErrorMessage);
    }

    [Fact]
    public async Task InvalidTailwindDirectiveDiagnostics_V3FlagsPreflight()
    {
        var errors = await GetErrorsAsync(
            new InvalidTailwindDirectiveDiagnostics(),
            "@tailwind preflight;",
            new ProjectCompletionValues { Version = TailwindVersion.V3 }
        );

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.InvalidTailwindDirective, error.ErrorType);
        Assert.Contains("Did you mean 'base'?", error.ErrorMessage);
        Assert.NotNull(error.Suggestion);
    }

    [Fact]
    public async Task InvalidConfigPathDiagnostics_FlagsUnknownThemePath()
    {
        var values = new ProjectCompletionValues { Version = TailwindVersion.V3 };
        values.SpacingMapper["4"] = "1rem";

        var errors = await GetErrorsAsync(
            new InvalidConfigPathDiagnostics(),
            ".x { margin: theme(spacing.5); }",
            values
        );

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.InvalidConfigPath, error.ErrorType);
        Assert.Contains("does not exist in your theme config", error.ErrorMessage);
    }

    [Fact]
    public async Task InvalidSourceDiagnostics_FlagsWindowsStylePath()
    {
        var errors = await GetErrorsAsync(
            new InvalidSourceDiagnostics(),
            "@import \"tailwindcss\" source(\"C:\\\\styles\");",
            new ProjectCompletionValues { Version = TailwindVersion.V4 }
        );

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.InvalidSource, error.ErrorType);
        Assert.Contains("Windows-style path", error.ErrorMessage);
    }

    [Fact]
    public async Task InvalidSourceDiagnostics_HandlesValidInlineSource()
    {
        var errors = await GetErrorsAsync(
            new InvalidSourceDiagnostics(),
            "@source inline(\"{p}-{px}\"); @source inline('{p}-{px}');",
            new ProjectCompletionValues { Version = TailwindVersion.V4 }
        );

        Assert.Empty(errors);
    }

    [Fact]
    public async Task InvalidSourceDiagnostics_FlagsInvalidInlineSource()
    {
        var errors = await GetErrorsAsync(
            new InvalidSourceDiagnostics(),
            """
@source none;
@source inline(invalid;
""",
            new ProjectCompletionValues { Version = TailwindVersion.V4 }
        );

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(ErrorType.InvalidSource, error.ErrorType));
        Assert.Contains(
            errors,
            error =>
                error.ErrorMessage.Contains("inline") && error.ErrorMessage.Contains("is invalid")
        );
        Assert.Contains(errors, error => error.ErrorMessage.Contains("source(none)"));
    }

    [Fact]
    public async Task UsedBlocklistClassDiagnostics_FlagsBlockedClass()
    {
        var values = new ProjectCompletionValues { Version = TailwindVersion.V3 };
        values.Blocklist.Add("foo");

        var errors = await GetErrorsAsync(
            new UsedBlocklistClassDiagnostics(),
            "<div class=\"foo bar foo\"></div>",
            values
        );

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(ErrorType.UsedBlocklistClass, error.ErrorType));
    }

    [Fact]
    public async Task CssConflictDiagnostics_FlagsConflictingClasses()
    {
        LinterUtilities.CheckForClassDuplicatesHandler = (_, _) =>
            [
                (
                    "text-red-500",
                    "'text-red-500' applies the same CSS properties as 'bg-red-500'.",
                    new[] { "bg-red-500" }
                ),
            ];

        var errors = await GetErrorsAsync(
            new CssConflictDiagnostics(),
            "<div class=\"text-red-500 bg-red-500\"></div>",
            new ProjectCompletionValues { Version = TailwindVersion.V3 }
        );

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.CssConflict, error.ErrorType);
        Assert.Contains("same CSS properties", error.ErrorMessage);
    }

    private static async Task<List<Error>> GetErrorsAsync(
        DiagnosticsChecker checker,
        string text,
        ProjectCompletionValues values
    )
    {
        var span = new SnapshotSpan(new FakeTextBuffer(text).CurrentSnapshot, 0, text.Length);
        Inject(checker, "_linterUtilities", new LinterUtilities());

        if (checker is InvalidConfigPathDiagnostics)
        {
            Inject(checker, "_projectConfigurationManager", new ProjectConfigurationManager());
        }

        return await CollectErrorsAsync(
            checker.GetErrorsAsync(
                span,
                values,
                ClassRegexHelper.GetClassesNormal,
                ClassRegexHelper.SplitNonRazorClasses,
                _ => false
            )
        );
    }

    [Fact]
    public async Task Aggregator_AwaitsDiagnosticsAndPreservesFilteringAndOrder()
    {
        var ready = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var cssOnly = new AsyncDiagnosticsChecker(Task.CompletedTask, true);
        var delayed = new AsyncDiagnosticsChecker(ready.Task);
        var blocked = new UsedBlocklistClassDiagnostics();
        foreach (var checker in new DiagnosticsChecker[] { cssOnly, delayed, blocked })
        {
            Inject(checker, "_linterUtilities", new LinterUtilities());
        }
        var aggregator = new DiagnosticsAggregator([cssOnly, delayed, blocked]);
        var snapshot = new FakeTextBuffer("<div class=\"foo\"></div>").CurrentSnapshot;
        var values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V3,
            Blocklist = ["foo"],
        };

        var collecting = CollectErrorsAsync(
            aggregator.GetErrorsAsync(
                new SnapshotSpan(snapshot, 0, snapshot.Length),
                false,
                values,
                ClassRegexHelper.GetClassesNormal,
                ClassRegexHelper.SplitNonRazorClasses,
                _ => false
            )
        );

        Assert.False(collecting.IsCompleted);
        ready.SetResult(true);
        var errors = await collecting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            new[] { ErrorType.CssConflict, ErrorType.UsedBlocklistClass },
            errors.Select(error => error.ErrorType)
        );
    }

    [Fact]
    public async Task DisabledDiagnostics_DoNotAwaitTheirImplementation()
    {
        LinterUtilities.GetErrorSeverityHandler = _ => ErrorSeverity.None;
        var ready = new TaskCompletionSource<bool>();

        var errors = await GetErrorsAsync(
                new AsyncDiagnosticsChecker(ready.Task),
                "<div class=\"foo\"></div>",
                new ProjectCompletionValues { Version = TailwindVersion.V3 }
            )
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(errors);
    }

    private static async Task<List<Error>> CollectErrorsAsync(IAsyncEnumerable<Error> errors)
    {
        var result = new List<Error>();
        await foreach (var error in errors)
        {
            result.Add(error);
        }
        return result;
    }

    private sealed class AsyncDiagnosticsChecker(Task ready, bool isCssOnly = false)
        : DiagnosticsChecker(isCssOnly, ErrorType.CssConflict)
    {
        protected override async IAsyncEnumerable<Error> GetErrorsImplAsync(
            SnapshotSpan span,
            ProjectCompletionValues projectCompletionValues,
            Func<string, IEnumerable<System.Text.RegularExpressions.Match>> findClasses,
            Func<string, IEnumerable<System.Text.RegularExpressions.Match>> splitClasses,
            Func<SnapshotSpan, bool> shouldNotAddErrors
        )
        {
            await ready;
            yield return new Error
            {
                Span = span.Snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeExclusive),
                ErrorType = ErrorType.CssConflict,
            };
        }
    }

    private static void Inject(object instance, string fieldName, object value)
    {
        var field = instance
            .GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        if (field is null)
        {
            field = typeof(DiagnosticsChecker).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
        }

        Assert.NotNull(field);
        field.SetValue(instance, value);
    }
}
