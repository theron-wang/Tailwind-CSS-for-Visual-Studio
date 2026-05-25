using System.Reflection;
using TailwindCSSIntellisense.Linting.Validators;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class CssValidatorTests
{
    [Theory]
    [InlineData("colors.blue.500", new[] { "colors", "blue", "500" })]
    [InlineData("spacing[2.5].value", new[] { "spacing", "2.5", "value" })]
    [InlineData("fontSize.sm", new[] { "fontSize", "sm" })]
    public void TokenizeTheme_SplitsDotNotationAndBracketSegments(string input, string[] expected)
    {
        var tokens = InvokePrivateStatic<List<string>>(typeof(CssValidator), "TokenizeTheme", input);

        Assert.Equal(expected, tokens);
    }

    [Theory]
    [InlineData("@tailwind utilities;", "@tailwind", true)]
    [InlineData("@tailwind base; @tailwind utilities;", "@tailwind", false)]
    [InlineData("@media screen(sm){}", "@screen", false)]
    public void HasOnlyOneDirective_DetectsSingleOccurrence(string text, string directive, bool expected)
    {
        var result = InvokePrivateStatic<bool>(typeof(CssValidator), "HasOnlyOneDirective", text, directive);

        Assert.Equal(expected, result);
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, args);
        Assert.NotNull(result);

        return (T)result!;
    }
}
