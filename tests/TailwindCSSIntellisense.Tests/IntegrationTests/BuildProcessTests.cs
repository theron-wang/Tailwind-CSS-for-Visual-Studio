using System.Diagnostics;
using System.Reflection;
using TailwindCSSIntellisense.Build;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Node;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

[Collection("Non-Parallel Tests")]
public class BuildProcessTests
{
    [Fact]
    public async Task RestartOneOutput_PreservesOtherActiveBuilds()
    {
        using var fixture = new Fixture();
        File.WriteAllText(
            Path.Combine(fixture.Root, "tailwind.cmd"),
            "@echo off\r\nset /p hold=\r\nexit /b 0\r\n"
        );
        var second = Path.Combine(fixture.Root, "second.css");
        File.WriteAllText(second, "@import 'tailwindcss';");
        fixture.Provider.Settings.BuildFiles.Add(new() { Input = second });
        fixture.Provider.Settings.BuildType = BuildProcessOptions.Manual;
        await fixture.Service.InitializeAsync();
        await fixture.Service.BuildAllAsync(BuildBehavior.Default);
        Assert.Equal(2, Processes(fixture.Service).Count);
        var entries = Processes(fixture.Service).ToArray();
        var first = entries[0];
        await (Task)
            typeof(TailwindBuildProcess)
                .GetMethod(
                    "RestartBuildProcessAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic
                )!
                .Invoke(fixture.Service, [first.Value, fixture.Values.FilePath, first.Key, false])!;
        Assert.Equal(2, Processes(fixture.Service).Count);
        Assert.NotSame(first.Value, Processes(fixture.Service)[first.Key]);
        Assert.Same(entries[1].Value, Processes(fixture.Service)[entries[1].Key]);
    }

    [Fact]
    public async Task SlowOutputReporting_DoesNotHoldLifecycleGate()
    {
        using var fixture = new Fixture();
        await fixture.Service.InitializeAsync();
        await fixture.Service.BuildAllAsync(BuildBehavior.Default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Community.VisualStudio.Toolkit.VS.WriteOutput = text =>
        {
            if (!text.Contains("slow output"))
                return Task.CompletedTask;
            entered.TrySetResult();
            return resume.Task;
        };
        General.Save(new() { VerboseBuild = true });
        try
        {
            fixture.Service.Emit(Assert.Single(Processes(fixture.Service)).Value, "slow output");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Service.EndAllProcessesAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(fixture.Service.AreProcessesActive());
        }
        finally
        {
            resume.TrySetResult();
            Community.VisualStudio.Toolkit.VS.WriteOutput = null;
        }
    }

    [Fact]
    public async Task FastExit_ReportsFinalOutputBeforeDisposingProcess()
    {
        using var fixture = new Fixture();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = Enumerable.Range(0, 200).Select(i => $"echo Done in {i}ms.");
        File.WriteAllLines(
            Path.Combine(fixture.Root, "tailwind.cmd"),
            new[] { "@echo off" }.Concat(messages).Append("exit /b 0")
        );
        fixture.Provider.Settings.BuildType = BuildProcessOptions.Manual;
        await fixture.Service.InitializeAsync();
        Community.VisualStudio.Toolkit.VS.WriteOutput = text =>
        {
            if (text.Contains("199ms"))
                reported.TrySetResult();
            return Task.CompletedTask;
        };
        try
        {
            await fixture.Service.BuildAllAsync(BuildBehavior.Default);
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            Community.VisualStudio.Toolkit.VS.WriteOutput = null;
        }
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("settings")]
    [InlineData("project")]
    public async Task LifecycleChange_WaitsForInFlightStartAndCleansUp(string action)
    {
        using var fixture = new Fixture();
        await fixture.Service.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manager.ProjectConfigurationInitializer.BeforeInitialize = async () =>
        {
            entered.TrySetResult();
            await resume.Task;
        };
        var build = fixture.Service.BuildAllAsync(BuildBehavior.Default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task change;
        if (action == "settings")
        {
            fixture.Provider.Settings.BuildType = BuildProcessOptions.None;
            change = fixture.Service.ApplySettings(fixture.Provider.Settings);
        }
        else if (action == "project")
        {
            fixture.Provider.Settings = fixture.MakeSettings();
            fixture.Provider.Settings.BuildFiles.Clear();
            change = fixture.Service.InitializeAsync(newProjectOpened: true);
        }
        else
        {
            change = fixture.Service.EndAllProcessesAsync();
        }
        try
        {
            Assert.False(change.IsCompleted);
        }
        finally
        {
            resume.TrySetResult();
        }
        await Task.WhenAll(build, change).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(fixture.Service.AreProcessesActive());
        Assert.Empty(Processes(fixture.Service));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsRestart_LateExitCannotRemoveOrDisposeReplacement(bool secondary)
    {
        using var fixture = new Fixture(secondary);
        await fixture.Service.InitializeAsync();
        await fixture.Service.BuildAllAsync(BuildBehavior.Default);
        var old = secondary
            ? Secondary(fixture.Service)!
            : Assert.Single(Processes(fixture.Service)).Value;
        await fixture.Service.ApplySettings(fixture.MakeSettings());
        var replacement = secondary
            ? Secondary(fixture.Service)!
            : Assert.Single(Processes(fixture.Service)).Value;
        Assert.NotSame(old, replacement);

        // Deliver the old exit notification after the settings restart, as can
        // happen when Process.Exited was queued while the lifecycle gate was held.
        await (Task)
            typeof(TailwindBuildProcess)
                .GetMethod(
                    "CleanupExitedProcessAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic
                )!
                .Invoke(fixture.Service, [old])!;
        Assert.False(replacement.HasExited);
        Assert.True(fixture.Service.AreProcessesActive());
        Assert.Same(
            replacement,
            secondary ? Secondary(fixture.Service) : Assert.Single(Processes(fixture.Service)).Value
        );
    }

    [Fact]
    public async Task MissingScript_RemainsMissingWhenCachedForSecondInput()
    {
        using var fixture = new Fixture(secondary: true);
        File.WriteAllText(Path.Combine(fixture.Root, "package.json"), "{\"scripts\":{}}");
        var input = Path.Combine(fixture.Root, "second.css");
        File.WriteAllText(input, "@import 'tailwindcss';");
        fixture.Provider.Settings.BuildFiles.Add(new() { Input = input });
        await fixture.Service.InitializeAsync();
        await fixture.Service.BuildAllAsync(BuildBehavior.Default);
        Assert.Equal(2, Processes(fixture.Service).Count);
        Assert.Null(Secondary(fixture.Service));
    }

    [Fact]
    public async Task RemovingLastBuildMapping_StopsExistingBuild()
    {
        using var fixture = new Fixture();
        await fixture.Service.InitializeAsync();
        await fixture.Service.BuildAllAsync(BuildBehavior.Default);
        Assert.True(fixture.Service.AreProcessesActive());
        var settings = fixture.MakeSettings();
        settings.BuildFiles.Clear();
        await fixture.Service.ApplySettings(settings);
        Assert.False(fixture.Service.AreProcessesActive());
        Assert.Empty(Processes(fixture.Service));
    }

    [Fact]
    public async Task Dispose_TerminatesRunningProcessAndIgnoresQueuedBuilds()
    {
        using var fixture = new Fixture();
        await fixture.Service.InitializeAsync();
        await fixture.Service.BuildAllAsync(BuildBehavior.Default);
        using var process = Process.GetProcessById(
            Assert.Single(Processes(fixture.Service)).Value.Id
        );
        fixture.Service.Dispose();
        Assert.True(process.WaitForExit(5000));
        await fixture.Service.BuildAllAsync(BuildBehavior.Default);
        Assert.False(fixture.Service.AreProcessesActive());
    }

    private static Dictionary<string, Process> Processes(TailwindBuildProcess service) =>
        (Dictionary<string, Process>)
            typeof(TailwindBuildProcess)
                .GetField("_outputFileToProcesses", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(service)!;

    private static Process? Secondary(TailwindBuildProcess service) =>
        (Process?)
            typeof(TailwindBuildProcess)
                .GetField("_secondaryProcess", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(service);

    private sealed class Probe : TailwindBuildProcess
    {
        public Task ApplySettings(TailwindSettings settings) => SettingsChangedAsync(settings);

        public void Emit(Process process, string data) => OutputDataReceived(process, data);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), $"tailwind-build-{Guid.NewGuid():N}");
        public ProjectCompletionValues Values { get; }
        public ProjectConfigurationManager Manager { get; } = new();
        public SettingsProvider Provider { get; } = new();
        public Probe Service { get; }
        private readonly bool _secondary;

        public Fixture(bool secondary = false)
        {
            _secondary = secondary;
            Directory.CreateDirectory(Root);
            Values = new()
            {
                FilePath = Path.Combine(Root, "input.css"),
                Version = TailwindVersion.V4,
            };
            File.WriteAllText(Values.FilePath, "@import 'tailwindcss';");
            // Local no-op commands keep the interactive shell alive without
            // depending on an installed Tailwind/npm or launching child watchers.
            File.WriteAllText(Path.Combine(Root, "tailwind.cmd"), "@echo off\r\nexit /b 0\r\n");
            File.WriteAllText(Path.Combine(Root, "npm.cmd"), "@echo off\r\nexit /b 0\r\n");
            File.WriteAllText(
                Path.Combine(Root, "package.json"),
                "{\"scripts\":{\"build\":\"unused\"}}"
            );
            Provider.Settings = MakeSettings();
            Manager.SeedDefault(Values);
            Service = new()
            {
                SettingsProvider = Provider,
                ProjectConfigurationManager = Manager,
                PackageJsonReader = new PackageJsonReader { SettingsProvider = Provider },
                Scanner = new ConfigFileScanner { FileFinder = new FileFinder() },
            };
        }

        public TailwindSettings MakeSettings(
            BuildProcessOptions type = BuildProcessOptions.OnSave
        ) =>
            new()
            {
                BuildType = type,
                ConfigurationFiles = [new() { Path = Values.FilePath }],
                BuildFiles = [new() { Input = Values.FilePath }],
                UseCli = true,
                TailwindCliPath = Path.Combine(Root, "tailwind.cmd"),
                BuildScript = "build",
                OverrideBuild = _secondary,
            };

        public void Dispose()
        {
            Service.Dispose();
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                    return;
                }
                catch (IOException ex)
                    when (elapsed.Elapsed < TimeSpan.FromSeconds(5)
                        && (ex.HResult & 0xFFFF) is 32 or 33
                    )
                {
                    // Windows can retain the shell's working-directory handle
                    // briefly after the process has signaled its exit.
                    Thread.Sleep(50);
                }
            }
        }
    }
}
