using System.Collections.Concurrent;
using System.Diagnostics;
using TailwindCSSIntellisense.Build;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

public class TailwindCliInteropIntegrationTests
{
    [Fact]
    public async Task Process_CapturesBothStreamsAndSupportsStandardInput()
    {
        var interop = new TestInterop();
        using var process = interop.StartShell();

        await process.StandardInput.WriteLineAsync("echo output-line");
        await process.StandardInput.WriteLineAsync("echo error-line>&2");
        await process.StandardInput.WriteLineAsync("exit");
        await WaitForOutputAsync(process);

        Assert.Contains(
            interop.Output,
            line => line.EndsWith("output-line", StringComparison.Ordinal)
        );
        Assert.Contains("error-line", interop.Output);
        Assert.Empty(interop.Messages);
    }

    [Fact]
    public async Task SecurityError_RetriesEachProcessOnceAndPreservesPolicyForOtherCommands()
    {
        var interop = new TestInterop();
        var retries = 0;
        using var first = interop.StartShell(retry: () => Interlocked.Increment(ref retries));
        using var second = interop.StartShell(retry: () => Interlocked.Increment(ref retries));

        foreach (var process in new[] { first, second })
        {
            await process.StandardInput.WriteLineAsync("echo PSSecurityException");
            await process.StandardInput.WriteLineAsync("echo PSSecurityException>&2");
            await process.StandardInput.WriteLineAsync("exit");
            await WaitForOutputAsync(process);
        }

        // A replacement process must not keep retrying if the fallback doesn't fix it.
        using var replacement = interop.StartShell(
            "/c echo PSSecurityException",
            () => Interlocked.Increment(ref retries)
        );
        await WaitForOutputAsync(replacement);

        Assert.Equal(2, retries);
        Assert.Single(interop.Messages);
        Assert.Contains("-ExecutionPolicy Unrestricted", interop.CanonicalizeStartInfo().Arguments);
        Assert.Contains(
            "npx @tailwindcss/cli canonicalize",
            interop.CanonicalizeStartInfo().Arguments
        );
    }

    [Fact]
    public async Task SecurityError_DoesNotRetryAnAlreadyUnrestrictedProcess()
    {
        var interop = new TestInterop();
        var retries = 0;
        using var process = interop.StartShell(
            "-NoProfile -ExecutionPolicy Unrestricted -Command \"Write-Output PSSecurityException\"",
            () => Interlocked.Increment(ref retries),
            powershell: true
        );
        await WaitForOutputAsync(process);

        Assert.Contains("PSSecurityException", interop.Output);
        Assert.Equal(0, retries);
        Assert.Empty(interop.Messages);
    }

    [Fact]
    public async Task StopProcess_StopsShellWithClosedStandardInput()
    {
        var interop = new TestInterop();
        using var process = interop.StartShell(
            "-NoProfile -Command \"Start-Sleep -Seconds 30\"",
            powershell: true
        );
        process.StandardInput.Close();

        interop.Stop(process);
        await WaitForOutputAsync(process);

        Assert.True(process.HasExited);
    }

    private static async Task WaitForOutputAsync(Process process)
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        // Also drain the asynchronous stdout/stderr readers.
        process.WaitForExit();
    }

    private sealed class TestInterop : TailwindCliInterop
    {
        public ConcurrentQueue<string> Output { get; } = new();
        public ConcurrentQueue<string> Messages { get; } = new();

        public TestInterop()
        {
            _settings = new TailwindSettings();
        }

        public Process StartShell(string args = "/q", Action? retry = null, bool powershell = false)
        {
            var startInfo = GetProcessStartInfo(Path.GetTempPath(), powershell);
            startInfo.Arguments = args;
            var process = StartProcess(startInfo);
            PostSetupProcess(process, retry);
            return process;
        }

        public ProcessStartInfo CanonicalizeStartInfo()
        {
            return GetCliProcessStartInfo(
                new ProjectCompletionValues
                {
                    Version = TailwindVersion.V4_2,
                    FilePath = Path.Combine(Path.GetTempPath(), "input.css"),
                },
                "canonicalize",
                powershell: true
            );
        }

        public void Stop(Process process) => StopProcess(process);

        protected override void OutputDataReceived(object sender, string data) =>
            Output.Enqueue(data);

        protected override void OnCliMessage(string message) => Messages.Enqueue(message);
    }
}
