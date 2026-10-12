using System.Reflection;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Linting.Validators.Canonicalization;
using TailwindCSSIntellisense.Tests.Stubs;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class CanonicalizerTests
{
    private const string FilePath = @"C:\project\index.html";

    [Fact]
    public async Task Cache_DoesNotReuseIndividualResultsForNewCombinations()
    {
        using var canonicalizer = new CanonicalizerStub();
        Assert.Empty(await canonicalizer.CanonicalizeAsync(["px-2"], FilePath));
        Assert.Empty(await canonicalizer.CanonicalizeAsync(["py-2"], FilePath));

        var result = Assert.Single(
            await canonicalizer.CanonicalizeAsync(["py-2", "px-2"], FilePath)
        );

        Assert.Equal(["px-2", "py-2"], result.ClassNames);
        Assert.Equal("p-2", result.CanonicalizedClassName);
    }

    [Fact]
    public async Task Cache_DoesNotReuseIncompleteGroups()
    {
        using var canonicalizer = new CanonicalizerStub();
        await canonicalizer.CanonicalizeAsync(["px-2", "py-2"], FilePath);

        Assert.Empty(await canonicalizer.CanonicalizeAsync(["px-2"], FilePath));
    }

    [Fact]
    public async Task Cache_NewGroupIncludesAllReplacementsOnFirstRun()
    {
        using var canonicalizer = new CanonicalizerStub();
        await canonicalizer.CanonicalizeAsync(["px-2"], FilePath);

        var result = await canonicalizer.CanonicalizeAsync(
            ["text-[#fff]", "py-2", "px-2"],
            FilePath
        );

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.CanonicalizedClassName == "p-2");
        Assert.Contains(result, c => c.CanonicalizedClassName == "text-white");
    }

    [Fact]
    public async Task Cache_ConcurrentIdenticalGroupsShareOneRequest()
    {
        using var canonicalizer = new CanonicalizerStub();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = canonicalizer.Handler;
        canonicalizer.Handler = async classes =>
        {
            started.TrySetResult();
            await release.Task;
            return await handler(classes);
        };
        var first = canonicalizer.CanonicalizeAsync(["px-2", "py-2"], FilePath);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var others = Enumerable
            .Range(0, 20)
            .Select(_ => canonicalizer.CanonicalizeAsync(["py-2", "px-2", "py-2"], FilePath))
            .ToArray();
        release.SetResult();

        var results = await Task.WhenAll(others.Prepend(first)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(results, r => Assert.Equal("p-2", Assert.Single(r).CanonicalizedClassName));
        Assert.Equal(3, canonicalizer.Requests.Count);
        results[0].Clear();
        Assert.Single(await canonicalizer.CanonicalizeAsync(["px-2", "py-2"], FilePath));
    }

    [Fact]
    public async Task Cache_FailedRequestsCanBeRetried()
    {
        using var canonicalizer = new CanonicalizerStub();
        var handler = canonicalizer.Handler;
        canonicalizer.Handler = _ => throw new InvalidOperationException("CLI failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath)
        );

        canonicalizer.Handler = handler;

        Assert.Single(await canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath));
    }

    [Fact]
    public async Task Cache_SettingsChangeClearsCompletedResults()
    {
        using var canonicalizer = new CanonicalizerStub();
        await canonicalizer.InitializeAsync();
        await canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath);
        canonicalizer.Handler = classes => Task.FromResult(classes);

        await canonicalizer.SettingsProvider.OnSettingsChanged!(new());

        Assert.Empty(await canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath));
        Assert.Equal(4, canonicalizer.Requests.Count);
    }

    [Fact]
    public async Task Cache_SettingsChangeDoesNotKeepInFlightResults()
    {
        using var canonicalizer = new CanonicalizerStub();
        await canonicalizer.InitializeAsync();
        var response = new TaskCompletionSource<string[]>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        canonicalizer.Handler = classes =>
            classes.Length == 0 ? Task.FromResult(Array.Empty<string>()) : response.Task;
        var pending = canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath);

        await canonicalizer.SettingsProvider.OnSettingsChanged!(new());
        canonicalizer.Handler = classes => Task.FromResult(classes);
        response.SetResult(["text-white"]);
        Assert.Single(await pending.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Empty(await canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath));
    }

    [Theory]
    [InlineData(TailwindVersion.V3)]
    [InlineData(TailwindVersion.V4)]
    [InlineData(TailwindVersion.V4_1)]
    public async Task Canonicalize_UnsupportedVersionsDoNotRequestCli(TailwindVersion version)
    {
        using var canonicalizer = new CanonicalizerStub(version);

        Assert.Empty(await canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath));
        Assert.Empty(canonicalizer.Requests);
    }

    [Theory]
    [InlineData(TailwindVersion.V4_2)]
    [InlineData(TailwindVersion.V4_3)]
    public async Task Canonicalize_SupportedVersionsReturnSingleClassReplacement(
        TailwindVersion version
    )
    {
        using var canonicalizer = new CanonicalizerStub(version);

        var result = Assert.Single(
            await canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath)
        );

        Assert.Equal(["text-[#fff]"], result.ClassNames);
        Assert.Equal("text-white", result.CanonicalizedClassName);
        Assert.Equal(2, canonicalizer.Requests.Count);
        Assert.Equal(["text-[#fff]"], canonicalizer.Requests[0].ClassNames);
        Assert.Empty(canonicalizer.Requests[1].ClassNames);
        Assert.All(canonicalizer.Requests, request => Assert.Equal(FilePath, request.FilePath));
    }

    [Theory]
    [InlineData("px-2", "py-2")]
    [InlineData("py-2", "px-2")]
    public async Task Canonicalize_CombinesClassesRegardlessOfInputOrder(
        string first,
        string second
    )
    {
        using var canonicalizer = new CanonicalizerStub();

        var result = Assert.Single(
            await canonicalizer.CanonicalizeAsync([first, "flex", second], FilePath)
        );

        Assert.Equal(["px-2", "py-2"], result.ClassNames);
        Assert.Equal("p-2", result.CanonicalizedClassName);
    }

    [Theory]
    [InlineData("px-2")]
    [InlineData("py-2")]
    [InlineData("flex")]
    [InlineData("custom-class")]
    public async Task Canonicalize_DoesNotSuggestUnchangedOrIncompleteGroups(string className)
    {
        using var canonicalizer = new CanonicalizerStub();

        Assert.Empty(await canonicalizer.CanonicalizeAsync([className], FilePath));
    }

    [Fact]
    public async Task Canonicalize_ReportsIndependentReplacementsAndIgnoresUnchangedClasses()
    {
        using var canonicalizer = new CanonicalizerStub();

        var results = await canonicalizer.CanonicalizeAsync(
            ["flex", "px-2", "text-[#fff]", "py-2", "custom-class"],
            FilePath
        );

        Assert.Equal(2, results.Count);
        Assert.Contains(
            results,
            c => c.CanonicalizedClassName == "p-2" && c.ClassNames.SequenceEqual(["px-2", "py-2"])
        );
        Assert.Contains(
            results,
            c =>
                c.CanonicalizedClassName == "text-white"
                && c.ClassNames.SequenceEqual(["text-[#fff]"])
        );
        Assert.Equal(6, canonicalizer.Requests.Count);
    }

    [Fact]
    public async Task Canonicalize_DeduplicatesCandidatesAndPreservesTheInputList()
    {
        using var canonicalizer = new CanonicalizerStub();
        List<string> classes = ["px-2", "py-2", "px-2", "py-2"];
        var original = classes.ToArray();

        var result = Assert.Single(await canonicalizer.CanonicalizeAsync(classes, FilePath));

        Assert.Equal("p-2", result.CanonicalizedClassName);
        Assert.Equal(["px-2", "py-2"], result.ClassNames);
        Assert.Equal(original, classes);
        Assert.Equal(3, canonicalizer.Requests.Count);
        Assert.All(
            canonicalizer.Requests,
            request => Assert.Equal(request.ClassNames.Distinct(), request.ClassNames)
        );
    }

    [Fact]
    public async Task Canonicalize_ExcludesRazorExpressionsFromCliRequests()
    {
        using var canonicalizer = new CanonicalizerStub();

        var result = Assert.Single(
            await canonicalizer.CanonicalizeAsync(
                ["@(enabled ? active : inactive)", "text-[#fff]"],
                FilePath
            )
        );

        Assert.Equal("text-white", result.CanonicalizedClassName);
        Assert.All(
            canonicalizer.Requests,
            request => Assert.DoesNotContain(request.ClassNames, c => c.Contains(' '))
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canonicalize_EmptyOrOnlyRazorInputDoesNotRequestCli(bool razorOnly)
    {
        using var canonicalizer = new CanonicalizerStub();

        Assert.Empty(
            await canonicalizer.CanonicalizeAsync(
                razorOnly ? ["@(enabled ? active : inactive)"] : [],
                FilePath
            )
        );
        Assert.Empty(canonicalizer.Requests);
    }

    [Fact]
    public async Task Canonicalize_AwaitsCliResponses()
    {
        var response = new TaskCompletionSource<string[]>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var canonicalizer = new CanonicalizerStub
        {
            Handler = classes =>
                classes.Length == 0 ? Task.FromResult(Array.Empty<string>()) : response.Task,
        };

        var canonicalizing = canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath);

        Assert.False(canonicalizing.IsCompleted);
        response.SetResult(["text-white"]);
        var result = Assert.Single(await canonicalizing.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("text-white", result.CanonicalizedClassName);
    }

    [Fact]
    public async Task Canonicalize_PropagatesCliFailures()
    {
        var failure = new InvalidOperationException("CLI failed");
        using var canonicalizer = new CanonicalizerStub
        {
            Handler = _ => Task.FromException<string[]>(failure),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            canonicalizer.CanonicalizeAsync(["text-[#fff]"], FilePath)
        );

        Assert.Same(failure, exception);
    }

    [Theory]
    [InlineData("{\"input\":\"px-2 py-2\",\"output\":\"p-2\",\"changed\":true}", "p-2")]
    [InlineData("  {\"input\":\"px-2 py-2\",\"output\":\"\"}", "")]
    public void Response_AcceptsMatchingJsonIncludingEmptyOutput(string line, string expected)
    {
        Assert.True(ReadResponse(line, "px-2 py-2", out var output));
        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData("Tailwind CSS banner")]
    [InlineData("{not valid json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"input\":\"other\",\"output\":\"p-2\"}")]
    [InlineData("{\"input\":\"PX-2 PY-2\",\"output\":\"p-2\"}")]
    [InlineData("{\"input\":12,\"output\":\"p-2\"}")]
    [InlineData("{\"input\":null,\"output\":\"p-2\"}")]
    [InlineData("{\"input\":\"px-2 py-2\"}")]
    [InlineData("{\"input\":\"px-2 py-2\",\"output\":null}")]
    [InlineData("{\"input\":\"px-2 py-2\",\"output\":42}")]
    public void Response_IgnoresNoiseMalformedOrUnmatchedResponses(string line)
    {
        Assert.False(ReadResponse(line, "px-2 py-2", out var output));
        Assert.Equal("", output);
    }

    private delegate bool ResponseReader(string line, string input, out string output);

    private static readonly ResponseReader ReadResponse = typeof(Canonicalizer)
        .GetMethod("TryReadResponse", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<ResponseReader>();
}
