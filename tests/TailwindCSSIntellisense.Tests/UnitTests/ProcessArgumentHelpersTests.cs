using TailwindCSSIntellisense.Helpers;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class ProcessArgumentHelpersTests
{
    [Theory]
    [InlineData("quote\"injection")]
    [InlineData("line\rbreak")]
    [InlineData("line\nbreak")]
    [InlineData("nul\0character")]
    public void CreateNodeProcessStartInfo_RejectsArgumentsThatBreakShellQuoting(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            ProcessArgumentHelpers.CreateNodeProcessStartInfo(
                Directory.GetCurrentDirectory(),
                value
            )
        );
    }

    [Theory]
    [InlineData("4.1.12", true)]
    [InlineData("4.2.0-beta.1", true)]
    [InlineData("4.2.0-rc.1+build.23", true)]
    [InlineData("", false)]
    [InlineData("latest", false)]
    [InlineData("4.1.12 & calc", false)]
    [InlineData("4.1.12\r\necho injected", false)]
    [InlineData("4.1.12%PATH%", false)]
    [InlineData("4.1.12\"", false)]
    [InlineData("--ignore-scripts", false)]
    public void IsNpmPackageVersion_OnlyAcceptsLiteralVersions(string value, bool expected)
    {
        Assert.Equal(expected, ProcessArgumentHelpers.IsNpmPackageVersion(value));
    }
}
