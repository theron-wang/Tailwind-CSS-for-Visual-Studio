namespace TailwindCSSIntellisense.Tests.UnitTests;

public class PathHelpersTests
{
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
