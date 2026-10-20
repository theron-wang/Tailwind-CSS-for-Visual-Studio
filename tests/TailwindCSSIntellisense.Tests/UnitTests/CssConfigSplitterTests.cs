using TailwindCSSIntellisense.Configuration;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class CssConfigSplitterTests
{
    [Theory]
    [InlineData("\"a;b\"")]
    [InlineData("\"a'b;c\"")]
    [InlineData("'a\"b;c'")]
    [InlineData("\"a\\\";b\"")]
    [InlineData("url(data:image/svg+xml;base64,abc)")]
    [InlineData("a\\;b")]
    [InlineData("\"a\\\\\"")]
    public void Split_PreservesSemicolonsWithinValues(string value)
    {
        Assert.Equal(
            new[] { $"--first: {value}", "--second: 2" },
            CssConfigSplitter.Split($"--first: {value}; --second: 2;")
        );
    }

    [Fact]
    public void Split_IgnoresEmptyDeclarations()
    {
        Assert.Equal(
            new[] { "--a: 1", "--b: 2" },
            CssConfigSplitter.Split(";  ; --a: 1; ; --b: 2 ;")
        );
    }
}
