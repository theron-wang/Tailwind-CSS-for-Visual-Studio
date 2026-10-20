using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

[Collection("Non-Parallel Tests")]
public class ConfigurationReloadTests
{
    [Fact]
    public async Task ExcludedDirectory_DoesNotExcludeSiblingWithSamePrefix()
    {
        using var fixture = new Fixture();
        var (_, project) = fixture.AddProject("app", "red");
        project.NotApplicablePaths = [Path.Combine(fixture.Root, "src")];
        var path = Path.Combine(fixture.Root, "src-other", "index.html");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.Same(project, await fixture.Manager.GetCompletionConfigurationByFilePathAsync(path));
    }

    [Fact]
    public async Task ImportTracking_ReplacesOldImportsAndRemovesDeletedProjects()
    {
        using var fixture = new Fixture();
        var (path, _) = fixture.AddProject("app", "red");
        var import = Path.Combine(fixture.Root, "shared.css");
        File.WriteAllText(import, "@utility shared { display: block; }");
        File.AppendAllText(path, "@import './shared.css';");
        await fixture.Manager.OnSettingsChangedAsync(fixture.Settings(path));
        var map =
            (Dictionary<string, HashSet<ConfigurationFile>>)
                typeof(ConfigurationFileReloader)
                    .GetField(
                        "_importToConfigurationFiles",
                        System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.NonPublic
                    )!
                    .GetValue(fixture.Loader.Reloader)!;
        Assert.Contains(import, map.Keys, StringComparer.OrdinalIgnoreCase);
        fixture.WriteConfig(path, "blue");
        await fixture.Loader.ReloadCustomAttributesAsync(
            new() { Path = path },
            fixture.Settings(path)
        );
        Assert.DoesNotContain(import, map.Keys, StringComparer.OrdinalIgnoreCase);
        File.AppendAllText(path, "@import './shared.css';");
        await fixture.Loader.ReloadCustomAttributesAsync(
            new() { Path = path },
            fixture.Settings(path)
        );
        Assert.Contains(import, map.Keys, StringComparer.OrdinalIgnoreCase);
        await fixture.Manager.OnSettingsChangedAsync(fixture.Settings());
        Assert.Empty(map);
    }

    [Fact]
    public async Task Reload_PublishesCompleteReplacementWithoutChangingExistingReaders()
    {
        using var fixture = new Fixture();
        var (path, old) = fixture.AddProject("app", "red");
        var pause = fixture.PauseNextTemplate();
        var reload = fixture.Loader.ReloadCustomAttributesAsync(
            new() { Path = path },
            fixture.Settings(path)
        );
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Same(
                old,
                await fixture.Manager.GetCompletionConfigurationByConfigFilePathAsync(path)
            );
            Assert.Equal("template", old.ColorMapper["brand"]);
            Assert.Empty(old.PluginClasses);
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        await reload;
        var current = (
            await fixture.Manager.GetCompletionConfigurationByConfigFilePathAsync(path)
        )!;
        Assert.NotSame(old, current);
        Assert.Equal("red", current.ColorMapper["brand"]);
        Assert.Equal("tw:", current.Prefix);
        Assert.Contains("local", current.PluginClasses);
        Assert.Equal("template", old.ColorMapper["brand"]);
        Assert.Equal("template", fixture.Template.ColorMapper["brand"]);
        Assert.Empty(fixture.Template.PluginClasses);
        Assert.Same(current, await fixture.Manager.GetCompletionConfigurationByFilePathAsync(null));
    }

    [Fact]
    public async Task NewerReload_WinsWhenOlderReloadFinishesLast()
    {
        using var fixture = new Fixture();
        var (path, _) = fixture.AddProject("app", "red");
        var pause = fixture.PauseNextTemplate();
        var older = fixture.Loader.ReloadCustomAttributesAsync(
            new() { Path = path },
            fixture.Settings(path)
        );
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            fixture.WriteConfig(path, "blue");
            await fixture.Loader.ReloadCustomAttributesAsync(
                new() { Path = path },
                fixture.Settings(path)
            );
            Assert.Equal(
                "blue",
                (
                    await fixture.Manager.GetCompletionConfigurationByConfigFilePathAsync(path)
                )!.ColorMapper["brand"]
            );
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        await older;
        Assert.Equal(
            "blue",
            (
                await fixture.Manager.GetCompletionConfigurationByConfigFilePathAsync(path)
            )!.ColorMapper["brand"]
        );
    }

    [Fact]
    public async Task FailedReload_KeepsLastCompleteSnapshot()
    {
        using var fixture = new Fixture();
        var (path, old) = fixture.AddProject("app", "red");
        fixture.Initializer.LoadTemplate = () =>
            Task.FromException<ProjectCompletionValues>(
                new InvalidDataException("Unavailable resource")
            );
        await fixture.Loader.ReloadCustomAttributesAsync(
            new() { Path = path },
            fixture.Settings(path)
        );
        Assert.Same(
            old,
            await fixture.Manager.GetCompletionConfigurationByConfigFilePathAsync(path)
        );
    }

    [Fact]
    public async Task RemovingProject_PreventsLateReloadFromRestoringIt()
    {
        using var fixture = new Fixture();
        var (path, _) = fixture.AddProject("app", "red");
        var pause = fixture.PauseNextTemplate();
        var reload = fixture.Loader.ReloadCustomAttributesAsync(
            new() { Path = path },
            fixture.Settings(path)
        );
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await fixture.Manager.OnSettingsChangedAsync(fixture.Settings());
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        await reload;
        Assert.Null(await fixture.Manager.GetCompletionConfigurationByConfigFilePathAsync(path));
        Assert.Empty(await fixture.Manager.GetAllProjectCompletionValuesAsync());
    }

    [Fact]
    public async Task SettingsReload_PublishesAllProjectsTogether()
    {
        using var fixture = new Fixture();
        var (first, oldFirst) = fixture.AddProject("first", "red");
        var (second, oldSecond) = fixture.AddProject("second", "blue");
        var pause = fixture.PauseNextTemplate();
        var reload = fixture.Manager.OnSettingsChangedAsync(fixture.Settings(first, second));
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal(
                new[] { oldFirst, oldSecond },
                await fixture.Manager.GetAllProjectCompletionValuesAsync()
            );
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        await reload;
        var projects = (await fixture.Manager.GetAllProjectCompletionValuesAsync()).ToArray();
        Assert.Equal(["red", "blue"], projects.Select(p => p.ColorMapper["brand"]));
        Assert.Equal("template", oldFirst.ColorMapper["brand"]);
        Assert.Equal("template", oldSecond.ColorMapper["brand"]);
    }

    [Fact]
    public async Task NewSettings_RejectOutdatedBatchBeforePublishing()
    {
        using var fixture = new Fixture();
        var (path, _) = fixture.AddProject("app", "red");
        var pause = fixture.PauseNextTemplate();
        var published = new List<int>();
        fixture.Loader.ConfigurationUpdated += async () =>
            published.Add((await fixture.Manager.GetAllProjectCompletionValuesAsync()).Count());
        var older = fixture.Manager.OnSettingsChangedAsync(fixture.Settings(path));
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var newer = fixture.Manager.OnSettingsChangedAsync(fixture.Settings());
        pause.Resume.TrySetResult();
        await Task.WhenAll(older, newer);
        Assert.Equal([0], published);
        Assert.Empty(await fixture.Manager.GetAllProjectCompletionValuesAsync());
    }

    [Fact]
    public async Task Lookup_DiscardsFallbackComputedBeforePublication()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "app.css");
        fixture.WriteConfig(path, "red");
        var pause = fixture.PauseNextTemplate();
        var lookupPath = Path.Combine(fixture.Root, "src", "index.html");
        var lookup = fixture.Manager.GetCompletionConfigurationByFilePathAsync(lookupPath);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await fixture.Manager.OnSettingsChangedAsync(fixture.Settings(path));
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        var result = await lookup;
        var published = await fixture.Manager.GetCompletionConfigurationByConfigFilePathAsync(path);
        Assert.Same(published, result);
        Assert.Same(
            published,
            await fixture.Manager.GetCompletionConfigurationByFilePathAsync(
                lookupPath.ToUpperInvariant()
            )
        );
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), $"tailwind-snapshots-{Guid.NewGuid():N}");
        public ProjectCompletionValues Template { get; } =
            new()
            {
                Version = TailwindVersion.V4_2,
                ColorMapper = new() { ["brand"] = "template" },
                Containers = new() { ["sm"] = "24rem" },
            };
        public ProjectConfigurationInitializer Initializer { get; }
        public ProjectConfigurationManager Manager { get; } = new();
        public CompletionConfiguration Loader { get; }
        private readonly DirectoryVersionFinder _finder = new();
        private readonly List<TaskCompletionSource> _pauses = [];

        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "src"));
            File.WriteAllText(Path.Combine(Root, "cli.cmd"), "@echo tailwindcss v4.2.0\r\n");
            Initializer = new() { Original = Template };
            Loader = new()
            {
                ProjectConfigurationInitializer = Initializer,
                ProjectConfigurationManager = Manager,
                DirectoryVersionFinder = _finder,
            };
            Loader.Reloader.SettingsProvider = new();
            Manager.Configuration = Loader;
            Manager.ProjectConfigurationInitializer = Initializer;
            Manager.DirectoryVersionFinder = _finder;
        }

        public void WriteConfig(string path, string color) =>
            File.WriteAllText(
                path,
                $"@import 'tailwindcss' prefix(tw) source('./src'); @theme {{ --color-brand: {color}; }} @utility local {{ display:block; }}"
            );

        public (string path, ProjectCompletionValues old) AddProject(string name, string color)
        {
            var path = Path.Combine(Root, name + ".css");
            WriteConfig(path, color);
            var old = Template.Copy();
            old.FilePath = path;
            old.ApplicablePaths = [Path.Combine(Root, "src")];
            Manager.Seed(path, old);
            Initializer.Original = Template;
            return (path, old);
        }

        public TailwindSettings Settings(params string[] paths) =>
            new()
            {
                ConfigurationFiles = [.. paths.Select(p => new ConfigurationFile { Path = p })],
                UseCli = true,
                TailwindCliPath = Path.Combine(Root, "cli.cmd"),
            };

        public (TaskCompletionSource Entered, TaskCompletionSource Resume) PauseNextTemplate()
        {
            var entered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var resume = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _pauses.Add(resume);
            var calls = 0;
            Initializer.LoadTemplate = async () =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.TrySetResult();
                    await resume.Task;
                }
                return Template;
            };
            return (entered, resume);
        }

        public void Dispose()
        {
            foreach (var pause in _pauses)
            {
                pause.TrySetResult();
            }

            Manager.Dispose();
            Loader.Reloader.Dispose();
            _finder.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}
