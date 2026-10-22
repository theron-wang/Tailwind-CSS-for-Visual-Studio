using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Linting.Validators.Canonicalization;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

[Collection("External processes")]
public class CanonicalizerCliIntegrationTests
{
    [Fact]
    public async Task FixtureCleanup_WaitsForTemporaryFileHandlesToClose()
    {
        await using var fixture = new CliFixture();
        using var file = File.Open(fixture.CssPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var cleanup = fixture.DisposeAsync().AsTask();

        Assert.False(cleanup.IsCompleted);
        file.Dispose();
        await cleanup.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.CssPath)));
    }

    [Fact]
    public async Task SettingsChange_InvalidatesCacheAndRestartsProcess()
    {
        await using var fixture = new CliFixture();
        using var canonicalizer = fixture.CreateCanonicalizer();
        await canonicalizer.CanonicalizeAsync(["first"], fixture.CssPath);
        await canonicalizer.CanonicalizeAsync(["first"], fixture.CssPath);
        Assert.Single(File.ReadAllLines(fixture.StartsLog));

        await canonicalizer.SettingsProvider.OnSettingsChanged!(
            canonicalizer.SettingsProvider.Settings
        );
        Assert.Single(await canonicalizer.CanonicalizeAsync(["first"], fixture.CssPath));

        Assert.Equal(2, File.ReadAllLines(fixture.StartsLog).Length);
    }

    [Fact]
    public async Task Request_IgnoresStartupNoiseAndMatchesItsInput()
    {
        await using var fixture = new CliFixture();
        using var canonicalizer = fixture.CreateCanonicalizer();

        var output = await fixture.RequestAsync(canonicalizer, "px-2", "py-2");

        Assert.Equal(["PX-2", "PY-2"], output);
        var process = Assert.Single(GetProcesses(canonicalizer)).Value;
        Assert.StartsWith("/d /q /c ", process.StartInfo.Arguments);
        Assert.Contains("--stream --format jsonl", process.StartInfo.Arguments);
    }

    [Fact]
    public async Task ConcurrentRequests_ReuseOneProcessWithoutMixingResponses()
    {
        await using var fixture = new CliFixture();
        using var canonicalizer = fixture.CreateCanonicalizer();
        var inputs = Enumerable.Range(0, 16).Select(i => $"request-{i}").ToArray();

        var results = await Task.WhenAll(
            inputs.Select(input => fixture.RequestAsync(canonicalizer, input))
        );

        for (var i = 0; i < inputs.Length; i++)
        {
            Assert.Equal([inputs[i].ToUpperInvariant()], results[i]);
        }
        Assert.Single(File.ReadAllLines(fixture.StartsLog));
    }

    [Fact]
    public async Task Request_DrainsStandardErrorAndPreservesEmptyResponses()
    {
        await using var fixture = new CliFixture();
        using var canonicalizer = fixture.CreateCanonicalizer();

        Assert.Equal(["STDERR"], await fixture.RequestAsync(canonicalizer, "stderr"));
        Assert.Empty(await fixture.RequestAsync(canonicalizer, "empty"));
    }

    [Fact]
    public async Task FailedRequest_RestartsProcessBeforeNextRequest()
    {
        await using var fixture = new CliFixture();
        using var canonicalizer = fixture.CreateCanonicalizer();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.RequestAsync(canonicalizer, "exit")
        );
        Assert.Equal(["RECOVERED"], await fixture.RequestAsync(canonicalizer, "recovered"));
        Assert.Equal(2, File.ReadAllLines(fixture.StartsLog).Length);
    }

    [Theory]
    [InlineData("first\nsecond")]
    [InlineData("first\rsecond")]
    [InlineData("first\r\nsecond")]
    public async Task Request_RejectsEmbeddedNewlinesBeforeStartingProcess(string className)
    {
        await using var fixture = new CliFixture();
        using var canonicalizer = fixture.CreateCanonicalizer();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.RequestAsync(canonicalizer, className)
        );

        Assert.False(File.Exists(fixture.StartsLog));
    }

    [Fact]
    public async Task Dispose_PreventsStartingAnotherProcess()
    {
        await using var fixture = new CliFixture();
        using var canonicalizer = fixture.CreateCanonicalizer();
        await fixture.RequestAsync(canonicalizer, "first");

        canonicalizer.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            fixture.RequestAsync(canonicalizer, "second")
        );
        Assert.Single(File.ReadAllLines(fixture.StartsLog));
    }

    private static ConcurrentDictionary<ProjectCompletionValues, Process> GetProcesses(
        Canonicalizer canonicalizer
    )
    {
        return (ConcurrentDictionary<ProjectCompletionValues, Process>)
            typeof(Canonicalizer)
                .GetField("_processes", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(canonicalizer)!;
    }

    private sealed class CliFixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "TailwindCanonicalizerTests",
            Guid.NewGuid().ToString("N")
        );
        private readonly string _cliPath;
        public string CssPath { get; }
        public string StartsLog { get; }

        public CliFixture()
        {
            Directory.CreateDirectory(_directory);
            CssPath = Path.Combine(_directory, "input.css");
            StartsLog = Path.Combine(_directory, "starts.log");
            _cliPath = Path.Combine(_directory, "fake-cli.cmd");
            var scriptPath = Path.Combine(_directory, "fake-cli.js");
            File.WriteAllText(CssPath, "@import 'tailwindcss';");
            File.WriteAllText(_cliPath, $"@echo off\r\nnode \"{scriptPath}\"\r\n");
            File.WriteAllText(
                scriptPath,
                $$"""
                const fs = require('node:fs');
                const readline = require('node:readline');
                fs.appendFileSync({{System.Text.Json.JsonSerializer.Serialize(
                    StartsLog
                )}}, `${process.pid}\n`);
                console.log('Microsoft Windows [Version 10.0.26200]');
                console.log('(c) 2026 Microsoft Corporation. All rights reserved.');
                console.log('C:\\>');
                const input = readline.createInterface({ input: process.stdin });
                input.on('line', requestText => {
                    if (requestText === 'exit') process.exit(7);
                    if (requestText === 'stderr') fs.writeSync(2, 'x'.repeat(131072) + '\n');
                    console.log('unrelated diagnostic');
                    console.log('{not valid json');
                    console.log('{"message":"unrelated JSON"}');
                    console.log('{"input":"other-request","output":"wrong"}');
                    console.log('{"input":12,"output":"wrong"}');
                    console.log(JSON.stringify({ input: requestText, output: null }));
                    const output = requestText === 'empty' ? '' : requestText.toUpperCase();
                    console.log(JSON.stringify({ input: requestText, output, changed: true }));
                });
                """
            );
        }

        public Canonicalizer CreateCanonicalizer()
        {
            var manager = new ProjectConfigurationManager();
            manager.Seed(
                CssPath,
                new ProjectCompletionValues { Version = TailwindVersion.V4_2, FilePath = CssPath }
            );
            var canonicalizer = new Canonicalizer
            {
                SettingsProvider = new SettingsProvider
                {
                    Settings = new TailwindSettings { UseCli = true, TailwindCliPath = _cliPath },
                },
            };
            typeof(Canonicalizer)
                .GetField(
                    "_projectConfigurationManager",
                    BindingFlags.Instance | BindingFlags.NonPublic
                )!
                .SetValue(canonicalizer, manager);
            return canonicalizer;
        }

        public Task<string[]> RequestAsync(Canonicalizer canonicalizer, params string[] classNames)
        {
            var method = typeof(Canonicalizer).GetMethod(
                "CanonicalizeViaCliAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            var request =
                (Func<IEnumerable<string>, string, Task<string[]>>)
                    method.CreateDelegate(
                        typeof(Func<IEnumerable<string>, string, Task<string[]>>),
                        canonicalizer
                    );
            return request(classNames, CssPath).WaitAsync(TimeSpan.FromSeconds(15));
        }

        public async ValueTask DisposeAsync()
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }
            // Closing the shell's input lets the fake CLI exit, but child processes
            // can retain their working directory briefly after the shell is stopped.
            if (File.Exists(StartsLog))
            {
                foreach (var pid in File.ReadAllLines(StartsLog))
                {
                    try
                    {
                        using var child = Process.GetProcessById(int.Parse(pid));
                        try
                        {
                            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        }
                        catch (TimeoutException)
                        {
                            child.Kill(entireProcessTree: true);
                            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        }
                    }
                    catch (ArgumentException)
                    {
                        // The fake CLI has already exited.
                    }
                }
            }
            // The launcher can still hold the working directory after its child exits.
            // Retry only sharing violations, and preserve the error if it persists.
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    Directory.Delete(_directory, recursive: true);
                    return;
                }
                catch (IOException ex)
                    when (elapsed.Elapsed < TimeSpan.FromSeconds(5)
                        && (ex.HResult & 0xFFFF) is 32 or 33
                    )
                {
                    await Task.Delay(50);
                }
            }
        }
    }
}
