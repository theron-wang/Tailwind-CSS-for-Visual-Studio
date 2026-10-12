using TailwindCSSIntellisense.Configuration.Descriptions;
using TailwindCSSIntellisense.Tests.Helpers;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class FontSizeDescriptionGeneratorTests
{
    [Theory]
    [InlineData("12px", "18px", "font-size: 12px;line-height: 18px;")]
    [InlineData(
        "12px",
        "{\"lineHeight\":\"18px\",\"letterSpacing\":\"1px\",\"fontWeight\":\"500\"}",
        "font-size: 12px;line-height: 18px;letter-spacing: 1px;font-weight: 500;"
    )]
    public void FontSizeDescription_EnumeratesInputOnce(
        string size,
        string metrics,
        string expected
    )
    {
        var input = new SinglePassEnumerable<string>([size, metrics]);

        Assert.Equal(expected, new FontSizeDescriptionGenerator().GetDescription(input));
        Assert.Equal(1, input.EnumerationCount);
    }
}
