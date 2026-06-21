using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

[Collection("Non-Parallel Tests")]
public class ClassRegexHelperTests : IDisposable
{
    private readonly Func<Task<TailwindSettings>>? _originalSettingsDelegate;

    public ClassRegexHelperTests()
    {
        _originalSettingsDelegate = ClassRegexHelper.GetTailwindSettings;
        ClassRegexHelper.GetTailwindSettings = null;
    }

    public void Dispose()
    {
        ClassRegexHelper.GetTailwindSettings = _originalSettingsDelegate;
    }

    [Fact]
    public void GetClassesNormal_FindsClassAttributes()
    {
        const string html =
            "<div class=\"px-4 text-red-500\"></div><span class='font-bold'></span>";

        var matches = ClassRegexHelper.GetClassesNormal(html).ToList();

        Assert.Equal(2, matches.Count);
        Assert.Equal("px-4 text-red-500", ClassRegexHelper.GetClassTextGroup(matches[0]).Value);
        Assert.Equal("font-bold", ClassRegexHelper.GetClassTextGroup(matches[1]).Value);
    }

    [Fact]
    public void GetClassesNormal_SplitsQuotedConditionalSegments()
    {
        const string html =
            "<div class=\"open ? 'px-2 text-red-500' : 'py-1 text-blue-500'\"></div>";

        var matches = ClassRegexHelper
            .GetClassesNormal(html)
            .Select(m => ClassRegexHelper.GetClassTextGroup(m).Value)
            .ToList();

        Assert.Equal(2, matches.Count);
        Assert.Contains("px-2 text-red-500", matches);
        Assert.Contains("py-1 text-blue-500", matches);
    }

    [Fact]
    public void GetClassesJavaScript_HandlesLargeMixedInput()
    {
        var input =
            new string('x', 6000) + "\n<div className=\"bg-blue-500 md:hover:text-white\"></div>";

        var matches = ClassRegexHelper.GetClassesJavaScript(input).ToList();

        Assert.Single(matches);
        Assert.Equal(
            "bg-blue-500 md:hover:text-white",
            ClassRegexHelper.GetClassTextGroup(matches[0]).Value
        );
    }

    [Fact]
    public void GetClassesRazor_ComplexCase()
    {
        var input =
            "\n<div class=\"@(\"hi\") bg-[''] bg-white @Func('h') @A.B(test ? \"test\" : \"foo\")\"></div>";

        var matches = ClassRegexHelper.GetClassesRazor(input).ToList();

        Assert.Single(matches);
        Assert.Equal(
            "@(\"hi\") bg-[''] bg-white @Func('h') @A.B(test ? \"test\" : \"foo\")",
            ClassRegexHelper.GetClassTextGroup(matches[0]).Value
        );
    }

    [Fact]
    public void SplitNonRazorClasses_SimpleSpaceSeparated_ReturnsEachClass()
    {
        const string text = "px-4 text-red-500 font-bold";

        var splits = ClassRegexHelper.SplitNonRazorClasses(text).ToList();

        Assert.Equal(3, splits.Count);
        Assert.Equal("px-4", splits[0].Value);
        Assert.Equal("text-red-500", splits[1].Value);
        Assert.Equal("font-bold", splits[2].Value);
    }

    [Fact]
    public void SplitNonRazorClasses_MultipleSpacesBetweenClasses_IgnoresExtraWhitespace()
    {
        const string text = "px-4   text-red-500";

        var splits = ClassRegexHelper.SplitNonRazorClasses(text).ToList();

        Assert.Equal(2, splits.Count);
        Assert.Equal("px-4", splits[0].Value);
        Assert.Equal("text-red-500", splits[1].Value);
    }

    [Fact]
    public void SplitNonRazorClasses_SingleClass_ReturnsSingleMatch()
    {
        const string text = "bg-white";

        var splits = ClassRegexHelper.SplitNonRazorClasses(text).ToList();

        Assert.Single(splits);
        Assert.Equal("bg-white", splits[0].Value);
    }

    [Fact]
    public void SplitNonRazorClasses_EmptyString_ReturnsNoMatches()
    {
        var splits = ClassRegexHelper.SplitNonRazorClasses("").ToList();

        Assert.Empty(splits);
    }

    [Fact]
    public void SplitNonRazorClasses_WhitespaceOnly_ReturnsNoMatches()
    {
        var splits = ClassRegexHelper.SplitNonRazorClasses("   ").ToList();

        Assert.Empty(splits);
    }

    [Fact]
    public void SplitNonRazorClasses_PreservesMatchIndices()
    {
        const string text = "px-4 text-sm";

        var splits = ClassRegexHelper.SplitNonRazorClasses(text).ToList();

        Assert.Equal(2, splits.Count);
        Assert.Equal(0, splits[0].Index);
        Assert.Equal(5, splits[1].Index);
    }

    [Fact]
    public void SplitNonRazorClasses_ClassWithModifier_TreatedAsOneToken()
    {
        const string text = "hover:bg-red-500 md:text-lg";

        var splits = ClassRegexHelper.SplitNonRazorClasses(text).ToList();

        Assert.Equal(2, splits.Count);
        Assert.Equal("hover:bg-red-500", splits[0].Value);
        Assert.Equal("md:text-lg", splits[1].Value);
    }

    [Fact]
    public void SplitNonRazorClasses_LeadingAndTrailingSpaces_ReturnsOnlyClasses()
    {
        const string text = "  bg-blue-500  ";

        var splits = ClassRegexHelper.SplitNonRazorClasses(text).ToList();

        Assert.Single(splits);
        Assert.Equal("bg-blue-500", splits[0].Value);
    }

    [Fact]
    public void SplitRazorClasses_SimpleNonRazorClasses_ReturnsEachClass()
    {
        const string text = "px-4 text-red-500";

        var splits = ClassRegexHelper.SplitRazorClasses(text).ToList();

        Assert.Equal(2, splits.Count);
        Assert.Equal("px-4", splits[0].Value);
        Assert.Equal("text-red-500", splits[1].Value);
    }

    [Fact]
    public void SplitRazorClasses_EmptyString_ReturnsNoMatches()
    {
        var splits = ClassRegexHelper.SplitRazorClasses("").ToList();

        Assert.Empty(splits);
    }

    [Fact]
    public void SplitRazorClasses_SingleClass_ReturnsSingleMatch()
    {
        var splits = ClassRegexHelper.SplitRazorClasses("bg-white").ToList();

        Assert.Single(splits);
        Assert.Equal("bg-white", splits[0].Value);
    }

    [Fact]
    public void SplitRazorClasses_RazorExpression_TreatedAsSingleToken()
    {
        // A Razor expression like @(Model.Class) should be treated as one token
        const string text = "px-4 @(Model.Class) text-sm";

        var splits = ClassRegexHelper.SplitRazorClasses(text).ToList();

        // The razor split regex groups @(...) as one token
        Assert.Contains(splits, s => s.Value.Contains("@(Model.Class)"));
    }

    [Fact]
    public void SplitRazorClasses_ClassWithModifier_TreatedAsOneToken()
    {
        const string text = "hover:bg-red-500 focus:ring-2";

        var splits = ClassRegexHelper.SplitRazorClasses(text).ToList();

        Assert.Equal(2, splits.Count);
        Assert.Equal("hover:bg-red-500", splits[0].Value);
        Assert.Equal("focus:ring-2", splits[1].Value);
    }

    [Fact]
    public void CustomRegexOverride_UsesConfiguredRegex()
    {
        ClassRegexHelper.GetTailwindSettings = () =>
            Task.FromResult(
                new TailwindSettings
                {
                    CustomRegexes = new CustomRegexes
                    {
                        HTML = new CustomRegexes.CustomRegex
                        {
                            Override = true,
                            Values = ["tw\\s*=\\s*\"(?<content>[^\"]+)\""],
                        },
                    },
                }
            );

        const string html = "<div tw=\"p-4 text-sm\" class=\"ignored\"></div>";

        try
        {
            var matches = ClassRegexHelper.GetClassesNormal(html).ToList();

            Assert.Single(matches);
            Assert.Equal("p-4 text-sm", ClassRegexHelper.GetClassTextGroup(matches[0]).Value);
        }
        finally
        {
            ClassRegexHelper.GetTailwindSettings = () => Task.FromResult(new TailwindSettings());
            ClassRegexHelper.GetClassesNormal(html).ToList(); // Trigger reset of custom regex
        }
    }
}
