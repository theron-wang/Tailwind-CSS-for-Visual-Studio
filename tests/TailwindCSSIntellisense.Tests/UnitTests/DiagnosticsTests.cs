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
    }

    [Fact]
    public void DeprecatedAtRuleDiagnostics_FlagsVariantAtRule()
    {
        var errors = GetErrors(
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
    public void InvalidScreenDiagnostics_FlagsUnknownScreen()
    {
        var values = new ProjectCompletionValues { Version = TailwindVersion.V4 };
        values.Breakpoints["md"] = "768px";

        var errors = GetErrors(new InvalidScreenDiagnostics(), "@screen lg { }", values);

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.InvalidScreen, error.ErrorType);
        Assert.Contains("does not exist", error.ErrorMessage);
    }

    [Fact]
    public void InvalidTailwindDirectiveDiagnostics_V3FlagsPreflight()
    {
        var errors = GetErrors(
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
    public void InvalidConfigPathDiagnostics_FlagsUnknownThemePath()
    {
        var values = new ProjectCompletionValues { Version = TailwindVersion.V3 };
        values.SpacingMapper["4"] = "1rem";

        var errors = GetErrors(
            new InvalidConfigPathDiagnostics(),
            ".x { margin: theme(spacing.5); }",
            values
        );

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.InvalidConfigPath, error.ErrorType);
        Assert.Contains("does not exist in your theme config", error.ErrorMessage);
    }

    [Fact]
    public void InvalidSourceDiagnostics_FlagsWindowsStylePath()
    {
        var errors = GetErrors(
            new InvalidSourceDiagnostics(),
            "@import \"tailwindcss\" source(\"C:\\\\styles\");",
            new ProjectCompletionValues { Version = TailwindVersion.V4 }
        );

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.InvalidSource, error.ErrorType);
        Assert.Contains("Windows-style path", error.ErrorMessage);
    }

    [Fact]
    public void InvalidSourceDiagnostics_HandlesValidInlineSource()
    {
        var errors = GetErrors(
            new InvalidSourceDiagnostics(),
            "@source inline(\"{p}-{px}\"); @source inline('{p}-{px}');",
            new ProjectCompletionValues { Version = TailwindVersion.V4 }
        );

        Assert.Empty(errors);
    }

    [Fact]
    public void InvalidSourceDiagnostics_FlagsInvalidInlineSource()
    {
        var errors = GetErrors(
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
    public void UsedBlocklistClassDiagnostics_FlagsBlockedClass()
    {
        var values = new ProjectCompletionValues { Version = TailwindVersion.V3 };
        values.Blocklist.Add("foo");

        var errors = GetErrors(
            new UsedBlocklistClassDiagnostics(),
            "<div class=\"foo bar foo\"></div>",
            values
        );

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(ErrorType.UsedBlocklistClass, error.ErrorType));
    }

    [Fact]
    public void CssConflictDiagnostics_FlagsConflictingClasses()
    {
        LinterUtilities.CheckForClassDuplicatesHandler = (_, _) =>
            [
                (
                    "text-red-500",
                    "'text-red-500' applies the same CSS properties as 'bg-red-500'.",
                    new[] { "bg-red-500" }
                ),
            ];

        var errors = GetErrors(
            new CssConflictDiagnostics(),
            "<div class=\"text-red-500 bg-red-500\"></div>",
            new ProjectCompletionValues { Version = TailwindVersion.V3 }
        );

        var error = Assert.Single(errors);
        Assert.Equal(ErrorType.CssConflict, error.ErrorType);
        Assert.Contains("same CSS properties", error.ErrorMessage);
    }

    private static List<Error> GetErrors(
        DiagnosticsChecker checker,
        string text,
        ProjectCompletionValues values
    )
    {
        var span = new SnapshotSpan(new StringTextSnapshot(text), 0, text.Length);
        Inject(checker, "_linterUtilities", new LinterUtilities());

        if (checker is InvalidConfigPathDiagnostics)
        {
            Inject(checker, "_projectConfigurationManager", new ProjectConfigurationManager());
        }

        return checker
            .GetErrors(
                span,
                values,
                ClassRegexHelper.GetClassesNormal,
                ClassRegexHelper.SplitNonRazorClasses,
                _ => false
            )
            .ToList();
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
        field!.SetValue(instance, value);
    }
}
