namespace TailwindCSSIntellisense.Tests.UnitTests;

public class ConfigurationThemeTokenizerTests
{
    [Theory]
    [InlineData("colors.blue.500", new[] { "colors", "blue", "500" })]
    [InlineData("spacing[2.5].value", new[] { "spacing", "2.5", "value" })]
    [InlineData("fontSize.sm", new[] { "fontSize", "sm" })]
    public void TokenizeTheme_SplitsDotNotationAndBracketSegments(string input, string[] expected)
    {
        var tokens = ConfigurationThemeTokenizer.TokenizeTheme(input).ToArray();

        Assert.Equal(expected, tokens);
    }
}
