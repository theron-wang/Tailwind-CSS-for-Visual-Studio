using System.Reflection;
using Community.VisualStudio.Toolkit;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Tests.UnitTests;

[Collection("Non-Parallel Tests")]
public class DirectoryVersionFinderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearCacheForDirectory_ClearsInstallationOnlyEntriesAndPreservesSiblingDirectories(
        bool trailingSlash
    )
    {
        using var finder = new DirectoryVersionFinder();
        var versions = GetCache<TailwindVersion>(finder, "_cache");
        var installed = GetCache<bool>(finder, "_installedCache");
        versions[@"c:\app"] = TailwindVersion.V3;
        versions[@"c:\app-old"] = TailwindVersion.V4;
        installed[@"c:\app"] = false;
        installed[@"c:\app\child"] = false;
        installed[@"c:\app-old"] = true;

        finder.ClearCacheForDirectory(trailingSlash ? @"C:\APP\" : @"C:\APP");

        Assert.Equal(@"c:\app-old", Assert.Single(versions).Key);
        Assert.Equal(@"c:\app-old", Assert.Single(installed).Key);
    }

    [Fact]
    public void ClearCacheForDirectory_NonRecursivePreservesDescendants()
    {
        using var finder = new DirectoryVersionFinder();
        var installed = GetCache<bool>(finder, "_installedCache");
        installed[@"c:\app"] = false;
        installed[@"c:\app\child"] = true;

        finder.ClearCacheForDirectory(@"C:\APP", recursive: false);

        Assert.Equal(@"c:\app\child", Assert.Single(installed).Key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningProjectOrFolder_InvalidatesInstallationAndVersionCaches(bool folder)
    {
        using var finder = new DirectoryVersionFinder();
        var versions = GetCache<TailwindVersion>(finder, "_cache");
        var installed = GetCache<bool>(finder, "_installedCache");
        versions[@"c:\app"] = TailwindVersion.V3;
        installed[@"c:\app"] = false;

        if (folder)
        {
            VS.Events.SolutionEvents.OpenFolder(@"c:\app");
        }
        else
        {
            VS.Events.SolutionEvents.OpenProject(new Project { FullPath = @"c:\app\app.csproj" });
        }

        Assert.Empty(versions);
        Assert.Empty(installed);
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("folder")]
    [InlineData("project")]
    public async Task Invalidation_DiscardsLateResultsAndDoesNotRemoveNewPendingCheck(
        string invalidation
    )
    {
        using var finder = new ControlledFinder();
        var directory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var settings = new TailwindCSSIntellisense.Settings.TailwindSettings();
        Assert.Null(
            finder.GetCachedTailwindInstallationStatusOrPopulateInBackground(directory, settings)
        );
        var oldCheck = Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.LastTask!;
        if (invalidation == "directory")
        {
            finder.ClearCacheForDirectory(directory);
        }
        else if (invalidation == "folder")
        {
            VS.Events.SolutionEvents.OpenFolder(directory);
        }
        else
        {
            VS.Events.SolutionEvents.OpenProject(
                new Project { FullPath = Path.Combine(directory, "app.csproj") }
            );
        }

        Assert.Null(
            finder.GetCachedTailwindInstallationStatusOrPopulateInBackground(directory, settings)
        );
        var newCheck = Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.LastTask!;
        finder.Checks[0].SetResult(false);
        await oldCheck;
        Assert.Empty(GetCache<bool>(finder, "_installedCache"));
        Assert.Null(
            finder.GetCachedTailwindInstallationStatusOrPopulateInBackground(directory, settings)
        );
        Assert.Equal(2, finder.Checks.Count);
        finder.Checks[1].SetResult(true);
        await newCheck;
        Assert.True(
            finder.GetCachedTailwindInstallationStatusOrPopulateInBackground(directory, settings)
        );
    }

    private sealed class ControlledFinder : DirectoryVersionFinder
    {
        public List<TaskCompletionSource<bool>> Checks { get; } = [];

        protected override Task<bool> IsTailwindInstalledAsync(string directory)
        {
            var completion = new TaskCompletionSource<bool>();
            Checks.Add(completion);
            return completion.Task;
        }
    }

    private static IDictionary<string, T> GetCache<T>(DirectoryVersionFinder finder, string name) =>
        (IDictionary<string, T>)
            typeof(DirectoryVersionFinder)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(finder)!;
}
