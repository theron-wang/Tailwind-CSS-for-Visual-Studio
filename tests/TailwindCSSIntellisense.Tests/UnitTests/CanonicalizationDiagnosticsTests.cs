using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Linting;
using TailwindCSSIntellisense.Linting.Validators.Diagnostics;
using TailwindCSSIntellisense.Tests.Stubs;

namespace TailwindCSSIntellisense.Tests.UnitTests;

[Collection("Non-Parallel Tests")]
public class CanonicalizationDiagnosticsTests
{
    private const string FilePath = @"C:\project\index.html";
    private static readonly ProjectCompletionValues Values = new()
    {
        Version = TailwindVersion.V4_2,
    };

    [Theory]
    [InlineData("html", "<div class=\"text-[#fff]\"></div>", "<div class=\"text-white\"></div>")]
    [InlineData(
        "javascript",
        "<div className=\"text-[#fff]\"></div>",
        "<div className=\"text-white\"></div>"
    )]
    [InlineData("css", ".x { @apply text-[#fff]; }", ".x { @apply text-white; }")]
    public async Task Diagnostics_ReportExactClassSpanAndReplacement(
        string language,
        string text,
        string expected
    )
    {
        using var canonicalizer = new CanonicalizerStub();
        var checker = CreateChecker(canonicalizer);
        var snapshot = new FakeTextBuffer(text, FilePath).CurrentSnapshot;

        var error = Assert.Single(
            await GetErrorsAsync(
                checker,
                snapshot,
                findClasses: language switch
                {
                    "javascript" => ClassRegexHelper.GetClassesJavaScript,
                    "css" => ClassRegexHelper.GetClassesCss,
                    _ => ClassRegexHelper.GetClassesNormal,
                }
            )
        );

        Assert.Equal(ErrorType.SuggestCanonicalClasses, error.ErrorType);
        Assert.Equal("'text-[#fff]' can be replaced with 'text-white'", error.ErrorMessage);
        var span = Assert.IsType<FakeTrackingSpan>(error.Span);
        Assert.Equal(text.IndexOf("text-[#fff]", StringComparison.Ordinal), span.Start);
        Assert.Equal("text-[#fff]".Length, span.Length);
        Assert.Equal(SpanTrackingMode.EdgeInclusive, span.Mode);
        var suggestion = Assert.IsType<Suggestion>(error.Suggestion);
        Assert.Equal("Replace 'text-[#fff]' with 'text-white'", suggestion.Message);
        var fix = Assert.Single(suggestion.SuggestedFix);
        Assert.Equal("text-white", fix.Replacement);
        Assert.Equal(
            SpanTrackingMode.EdgeExclusive,
            Assert.IsType<FakeTrackingSpan>(fix.ApplicableTo).Mode
        );
        Assert.Equal(expected, ApplyFixes(text, suggestion));
        Assert.All(canonicalizer.Requests, request => Assert.Equal(FilePath, request.FilePath));
    }

    [Theory]
    [InlineData("px-2 py-2", "p-2", "p-2")]
    [InlineData("py-2 px-2", "p-2", "p-2")]
    [InlineData("px-2 flex py-2", "p-2 flex", "flex p-2")]
    public async Task Diagnostics_GroupFixReplacesSelectedClassAndRemovesOtherClasses(
        string classes,
        string firstFix,
        string secondFix
    )
    {
        var text = $"<div class=\"{classes}\"></div>";
        using var canonicalizer = new CanonicalizerStub();
        var snapshot = new FakeTextBuffer(text, FilePath).CurrentSnapshot;

        var errors = await GetErrorsAsync(CreateChecker(canonicalizer), snapshot);

        Assert.Equal(2, errors.Count);
        Assert.Equal(["px-2", "py-2"], errors.Select(e => GetText(text, e.Span)).OrderBy(c => c));
        Assert.All(
            errors,
            error =>
            {
                Assert.Equal(ErrorType.SuggestCanonicalClasses, error.ErrorType);
                Assert.Equal("'px-2 py-2' can be replaced with 'p-2'", error.ErrorMessage);
                var fixes = Assert.IsType<Suggestion>(error.Suggestion).SuggestedFix.ToList();
                Assert.Equal(2, fixes.Count);
                Assert.Single(fixes, f => f.Replacement == "p-2");
                Assert.Single(fixes, f => f.Replacement == "");
            }
        );
        Assert.Equal($"<div class=\"{firstFix}\"></div>", ApplyFixes(text, errors[0].Suggestion!));
        Assert.Equal($"<div class=\"{secondFix}\"></div>", ApplyFixes(text, errors[1].Suggestion!));
    }

    [Fact]
    public async Task Diagnostics_RepeatedClassesHaveDistinctSpansAndOneReplacementPerFix()
    {
        const string text = "<div class=\"text-[#fff] text-[#fff]\"></div>";
        using var canonicalizer = new CanonicalizerStub();
        var snapshot = new FakeTextBuffer(text, FilePath).CurrentSnapshot;

        var errors = await GetErrorsAsync(CreateChecker(canonicalizer), snapshot);

        Assert.Equal(2, errors.Count);
        Assert.Equal(
            new[]
            {
                text.IndexOf("text-[#fff]", StringComparison.Ordinal),
                text.LastIndexOf("text-[#fff]", StringComparison.Ordinal),
            },
            errors.Select(e => Assert.IsType<FakeTrackingSpan>(e.Span).Start)
        );
        Assert.All(
            errors,
            error =>
                Assert.Equal(
                    "<div class=\"text-white\"></div>",
                    ApplyFixes(text, error.Suggestion!)
                )
        );
    }

    [Fact]
    public async Task Diagnostics_ExactClassLocationsDoNotIncludeVariantSubstrings()
    {
        const string text = "<div class=\"hover:text-[#fff] text-[#fff]\"></div>";
        using var canonicalizer = new CanonicalizerStub();
        var snapshot = new FakeTextBuffer(text, FilePath).CurrentSnapshot;

        var error = Assert.Single(await GetErrorsAsync(CreateChecker(canonicalizer), snapshot));

        Assert.Equal(
            text.LastIndexOf("text-[#fff]", StringComparison.Ordinal),
            Assert.IsType<FakeTrackingSpan>(error.Span).Start
        );
        Assert.Equal(
            "<div class=\"hover:text-[#fff] text-white\"></div>",
            ApplyFixes(text, error.Suggestion!)
        );
    }

    [Fact]
    public async Task Diagnostics_ExpandsPartialSpanToItsContainingClassAttribute()
    {
        const string text =
            "<div class=\"text-[#fff]\"></div>\n<button class=\"px-2 py-2\"></button>";
        using var canonicalizer = new CanonicalizerStub();
        var snapshot = new FakeTextBuffer(text, FilePath).CurrentSnapshot;
        var requestedSpan = new SnapshotSpan(
            snapshot,
            text.IndexOf("px-2", StringComparison.Ordinal),
            4
        );

        var errors = await GetErrorsAsync(CreateChecker(canonicalizer), snapshot, requestedSpan);

        Assert.Equal(["px-2", "py-2"], errors.Select(e => GetText(text, e.Span)));
        Assert.All(
            errors,
            error =>
                Assert.Equal(
                    "<div class=\"text-[#fff]\"></div>\n<button class=\"p-2\"></button>",
                    ApplyFixes(text, error.Suggestion!)
                )
        );
        Assert.DoesNotContain(
            canonicalizer.Requests.SelectMany(r => r.ClassNames),
            c => c == "text-[#fff]"
        );
    }

    [Fact]
    public async Task Diagnostics_SkipsAlreadyCheckedLocations()
    {
        const string text = "<div class=\"text-[#fff] text-[#fff]\"></div>";
        using var canonicalizer = new CanonicalizerStub();
        var snapshot = new FakeTextBuffer(text, FilePath).CurrentSnapshot;
        var skippedStart = text.IndexOf("text-[#fff]", StringComparison.Ordinal);

        var error = Assert.Single(
            await GetErrorsAsync(
                CreateChecker(canonicalizer),
                snapshot,
                shouldNotAddErrors: span => span.Start.Position == skippedStart
            )
        );

        Assert.Equal(
            text.LastIndexOf("text-[#fff]", StringComparison.Ordinal),
            Assert.IsType<FakeTrackingSpan>(error.Span).Start
        );
        var fix = Assert.Single(error.Suggestion!.SuggestedFix);
        Assert.Equal("text-white", fix.Replacement);
        Assert.Equal(
            "<div class=\"text-[#fff] text-white\"></div>",
            ApplyFixes(text, error.Suggestion)
        );
    }

    [Theory]
    [InlineData("<div></div>")]
    [InlineData("<div class=\"\"></div>")]
    [InlineData("<div class=\"   \"></div>")]
    public async Task Diagnostics_EmptyOrMissingScopeDoesNotRequestCanonicalization(string text)
    {
        using var canonicalizer = new CanonicalizerStub();
        var snapshot = new FakeTextBuffer(text, FilePath).CurrentSnapshot;

        Assert.Empty(await GetErrorsAsync(CreateChecker(canonicalizer), snapshot));
        Assert.Empty(canonicalizer.Requests);
    }

    [Fact]
    public async Task Diagnostics_AlreadyCanonicalClassesProduceNoErrors()
    {
        using var canonicalizer = new CanonicalizerStub();
        var snapshot = new FakeTextBuffer(
            "<div class=\"p-2 text-white flex\"></div>",
            FilePath
        ).CurrentSnapshot;

        Assert.Empty(await GetErrorsAsync(CreateChecker(canonicalizer), snapshot));
    }

    [Fact]
    public async Task Diagnostics_DisabledSeverityDoesNotRequestCanonicalization()
    {
        using var canonicalizer = new CanonicalizerStub();
        var snapshot = new FakeTextBuffer(
            "<div class=\"text-[#fff]\"></div>",
            FilePath
        ).CurrentSnapshot;
        var original = LinterUtilities.GetErrorSeverityHandler;
        LinterUtilities.GetErrorSeverityHandler = _ => ErrorSeverity.None;
        try
        {
            Assert.Empty(await GetErrorsAsync(CreateChecker(canonicalizer), snapshot));
            Assert.Empty(canonicalizer.Requests);
        }
        finally
        {
            LinterUtilities.GetErrorSeverityHandler = original;
        }
    }

    private static CanonicalizationDiagnostics CreateChecker(CanonicalizerStub canonicalizer)
    {
        var checker = new CanonicalizationDiagnostics();
        typeof(CanonicalizationDiagnostics)
            .GetField("_canonicalizer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(checker, canonicalizer);
        typeof(DiagnosticsChecker)
            .GetField("_linterUtilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(checker, new LinterUtilities());
        return checker;
    }

    private static async Task<List<Error>> GetErrorsAsync(
        CanonicalizationDiagnostics checker,
        ITextSnapshot snapshot,
        SnapshotSpan? requestedSpan = null,
        Func<string, IEnumerable<Match>>? findClasses = null,
        Func<SnapshotSpan, bool>? shouldNotAddErrors = null
    )
    {
        var errors = new List<Error>();
        await foreach (
            var error in checker.GetErrorsAsync(
                requestedSpan ?? new SnapshotSpan(snapshot, 0, snapshot.Length),
                Values,
                findClasses ?? ClassRegexHelper.GetClassesNormal,
                ClassRegexHelper.SplitNonRazorClasses,
                shouldNotAddErrors ?? (_ => false)
            )
        )
        {
            errors.Add(error);
        }
        return errors;
    }

    private static string GetText(string text, ITrackingSpan trackingSpan)
    {
        var span = Assert.IsType<FakeTrackingSpan>(trackingSpan);
        return text.Substring(span.Start, span.Length);
    }

    private static string ApplyFixes(string text, Suggestion suggestion)
    {
        var previousStart = text.Length;
        foreach (
            var fix in suggestion.SuggestedFix.OrderByDescending(f =>
                Assert.IsType<FakeTrackingSpan>(f.ApplicableTo).Start
            )
        )
        {
            var span = Assert.IsType<FakeTrackingSpan>(fix.ApplicableTo);
            Assert.True(
                span.Start + span.Length <= previousStart,
                "Suggested edits must not overlap."
            );
            text = text.Remove(span.Start, span.Length).Insert(span.Start, fix.Replacement);
            previousStart = span.Start;
        }
        return text;
    }
}
