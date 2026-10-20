using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class UtilityHelpersTests
{
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
