using System.Globalization;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class PathHelpersTests
{
    [Theory]
    [InlineData(@"D:\styles\input.css")]
    [InlineData(@"\\other-server\share\input.css")]
    public void Paths_RoundTripFilesOutsideTheBaseVolume(string absolute)
    {
        const string directory = @"C:\project";
        Assert.Equal(
            absolute,
            PathHelpers.GetAbsolutePath(directory, PathHelpers.GetRelativePath(absolute, directory))
        );
    }

    [Theory]
    [InlineData("input#1.css")]
    [InlineData("input%20.css")]
    [InlineData("input #1%20.css")]
    [InlineData("styles/input.css")]
    [InlineData("../shared/input.css")]
    public void Paths_RoundTripLiteralFileNames(string relative)
    {
        var directory = Path.Combine(Path.GetTempPath(), "project #1%20");
        var expected = Path.GetFullPath(Path.Combine(directory, relative));

        Assert.Equal(expected, PathHelpers.GetAbsolutePath(directory, relative));
        Assert.Equal(
            expected,
            PathHelpers.GetAbsolutePath(directory, PathHelpers.GetRelativePath(expected, directory))
        );
    }

    [Theory]
    [InlineData("src/file.html", "src/**/*.html", true)]
    [InlineData("src/views/file.html", "src/**/*.html", true)]
    [InlineData("src/file.css", "src/**/*.html", false)]
    [InlineData("src/a.html", "src/?.html", true)]
    [InlineData("src//.html", "src/?.html", false)]
    [InlineData("src/file+1.html", "src/{file+1,other}.html", true)]
    [InlineData("src/file111.html", "src/{file+1,other}.html", false)]
    [InlineData("src/file[1].html", "src/{file[1],other}.html", true)]
    [InlineData("src/file1.html", "src/{file[1],other}.html", false)]
    [InlineData("src/page.html", "src/{*.html,*.razor}", true)]
    [InlineData("/a/b/c/file.html", "/a/**/{file,other}.html", true)]
    [InlineData("/a/b/c/file.css", "/a/**/{file,other}.html", false)]
    public void PathMatchesGlob_HandlesDirectoriesAndLiteralAlternatives(
        string path,
        string pattern,
        bool expected
    )
    {
        Assert.Equal(expected, PathHelpers.PathMatchesGlob(path, pattern));
    }

    [Fact]
    public void PathMatchesGlob_IsIndependentOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.True(PathHelpers.PathMatchesGlob("SRC/INPUT.HTML", "src/*.html"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ContainingDirectory_ResolvesRelativeFilePaths()
    {
        Assert.Equal(
            Directory.GetCurrentDirectory(),
            PathHelpers.GetContainingDirectory("input.css")
        );
        Assert.Equal(
            Path.Combine(Directory.GetCurrentDirectory(), "styles"),
            PathHelpers.GetContainingDirectory(Path.Combine("styles", "input.css"))
        );
    }

    [Fact]
    public void ContainingDirectory_RejectsPathsWithoutAParent()
    {
        Assert.Throws<ArgumentException>(() =>
            PathHelpers.GetContainingDirectory(Path.GetPathRoot(Directory.GetCurrentDirectory())!)
        );
    }
}
