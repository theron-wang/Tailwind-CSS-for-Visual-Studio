namespace TailwindCSSIntellisense.Tests.UnitTests;

public class BraceExpanderTests
{
    [Theory]
    [InlineData("{a,a}", 20)]
    [InlineData("{a}", 200)]
    public void Expand_BoundsDuplicateExpansionWorkAndRecursion(string part, int count)
    {
        Assert.Throws<ArgumentException>(() =>
            BraceExpander.Expand(string.Concat(Enumerable.Repeat(part, count)))
        );
    }

    [Theory]
    [InlineData("p-{1..3}", "p-1 p-2 p-3")]
    [InlineData("p-{3..1}", "p-1 p-2 p-3")]
    [InlineData("p-{1..5..2}", "p-1 p-3 p-5")]
    [InlineData("p-{2147483646..2147483647}", "p-2147483646 p-2147483647")]
    [InlineData("{hover:,}bg-{red,blue}", "bg-blue bg-red hover:bg-blue hover:bg-red")]
    [InlineData("{a,{b,c}}-{1,2}", "a-1 a-2 b-1 b-2 c-1 c-2")]
    [InlineData("p-{1..3}-{x,y}", "p-1-x p-1-y p-2-x p-2-y p-3-x p-3-y")]
    public void Expand_ExpandsRangesAndNestedAlternatives(string input, string expected)
    {
        Assert.Equal(expected.Split(' '), BraceExpander.Expand(input).Order());
    }

    [Theory]
    [InlineData("p-{1..3..0}")]
    [InlineData("p-{1..999999999999999999999}")]
    [InlineData("p-{2147483647..0}")]
    [InlineData("p-{0..2147483647}")]
    [InlineData("p-{1..1000}-{1..1000}")]
    public void Expand_RejectsInvalidRangesWithoutLoopingOrOverflowing(string input)
    {
        Assert.Throws<ArgumentException>(() => BraceExpander.Expand(input));
    }
}
