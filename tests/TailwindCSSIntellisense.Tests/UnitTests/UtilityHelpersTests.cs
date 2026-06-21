using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class UtilityHelpersTests
{
    [Fact]
    public void BraceExpander_ExpandsNestedAndRangePatterns()
    {
        var expanded = BraceExpander.Expand("p-{1..3}-{x,y}");

        Assert.Equal(6, expanded.Count);
        Assert.Contains("p-1-x", expanded);
        Assert.Contains("p-3-y", expanded);
    }

    [Fact]
    public void CssConfigSplitter_IgnoresSemicolonsInsideQuotedStrings()
    {
        const string css = "--a: 1; --b: \"a;b\"; --c: 3";

        var parts = CssConfigSplitter.Split(css).ToList();

        Assert.Equal(3, parts.Count);
        Assert.Equal("--b: \"a;b\"", parts[1]);
    }

    [Fact]
    public void CssSizeConverter_ConvertsKnownUnitsAndRejectsInvalid()
    {
        Assert.Equal(16, CssSizeConverter.CssSizeToPixels("1rem"));
        Assert.Equal(384, CssSizeConverter.CssSizeToPixels("20vw"));
        Assert.Equal(0, CssSizeConverter.CssSizeToPixels("abc"));
    }

    [Fact]
    public void ImportantModifierHelper_DetectsImportantModifierSafely()
    {
        Assert.True(ImportantModifierHelper.IsImportantModifier("!text-red-500"));
        Assert.True(ImportantModifierHelper.IsImportantModifier("text-red-500!"));
        Assert.False(ImportantModifierHelper.IsImportantModifier("!!text-red-500"));
    }

    [Fact]
    public void PathHelpers_PathMatchesGlob_SupportsStarDoubleStarAndBraces()
    {
        Assert.True(PathHelpers.PathMatchesGlob("/a/b/c/file.html", "/a/**/{file,other}.html"));
        Assert.False(PathHelpers.PathMatchesGlob("/a/b/c/file.css", "/a/**/{file,other}.html"));
    }

    [Fact]
    public void CliUsageValidator_ValidatesCliPathWhenEnabled()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.True(
                CliUsageValidator.IsCliUsedCorrectly(new TailwindSettings { UseCli = false })
            );
            Assert.True(
                CliUsageValidator.IsCliUsedCorrectly(
                    new TailwindSettings { UseCli = true, TailwindCliPath = tempFile }
                )
            );
            Assert.False(
                CliUsageValidator.IsCliUsedCorrectly(
                    new TailwindSettings { UseCli = true, TailwindCliPath = tempFile + ".missing" }
                )
            );
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
