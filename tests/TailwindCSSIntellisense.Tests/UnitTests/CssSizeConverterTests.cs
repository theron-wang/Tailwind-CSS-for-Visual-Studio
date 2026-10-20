namespace TailwindCSSIntellisense.Tests.UnitTests;

public class CssSizeConverterTests
{
    [Theory]
    [InlineData("1..2rem")]
    [InlineData(".px")]
    [InlineData("12,5px")]
    [InlineData("calc(1rem + 2px)")]
    public void CssSizeToPixels_InvalidValuesReturnZero(string input)
    {
        Assert.Equal(0, CssSizeConverter.CssSizeToPixels(input));
    }

    [Theory]
    [InlineData("-1.5rem", -24)]
    [InlineData("+2px", 2)]
    [InlineData(".5rem", 8)]
    public void CssSizeToPixels_ParsesSignedAndFractionalValues(string input, double expected)
    {
        Assert.Equal(expected, CssSizeConverter.CssSizeToPixels(input));
    }
}
