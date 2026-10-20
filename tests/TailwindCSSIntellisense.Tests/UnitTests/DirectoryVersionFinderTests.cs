using System.Reflection;
using Community.VisualStudio.Toolkit;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Tests.UnitTests;

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

    private static IDictionary<string, T> GetCache<T>(DirectoryVersionFinder finder, string name) =>
        (IDictionary<string, T>)
            typeof(DirectoryVersionFinder)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(finder)!;
}
