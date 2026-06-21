using TailwindCSSIntellisense.Helpers;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class StringHelpersTests
{
    #region ReplaceLastOccurrence

    [Fact]
    public void ReplaceLastOccurrence_SingleOccurrence_ReplacesIt()
    {
        var result = "bg-red-500".ReplaceLastOccurrence("red", "{0}");

        Assert.Equal("bg-{0}-500", result);
    }

    [Fact]
    public void ReplaceLastOccurrence_MultipleOccurrences_ReplacesOnlyLast()
    {
        // This is the key bug fix: border-border should have last 'border' replaced
        var result = "border-border".ReplaceLastOccurrence("border", "{0}");

        Assert.Equal("border-{0}", result);
    }

    [Fact]
    public void ReplaceLastOccurrence_SubstringNotFound_ReturnsOriginal()
    {
        var result = "bg-red-500".ReplaceLastOccurrence("blue", "{0}");

        Assert.Equal("bg-red-500", result);
    }

    [Fact]
    public void ReplaceLastOccurrence_EmptySource_ReturnsEmpty()
    {
        var result = "".ReplaceLastOccurrence("red", "{0}");

        Assert.Equal("", result);
    }

    [Fact]
    public void ReplaceLastOccurrence_FindIsEmpty_ReturnsOriginal()
    {
        // LastIndexOf("") returns source.Length on most .NET implementations
        // but the behavior should be predictable
        var source = "hello";
        var result = source.ReplaceLastOccurrence("", "X");

        // When find is "" - LastIndexOf returns source.Length
        // Remove(source.Length, 0) + Insert(source.Length, "X") → "helloX"
        Assert.Equal("helloX", result);
    }

    [Fact]
    public void ReplaceLastOccurrence_FindEqualsWholeString_ReplacesWhole()
    {
        var result = "text-red-500".ReplaceLastOccurrence("text-red-500", "{class}");

        Assert.Equal("{class}", result);
    }

    [Fact]
    public void ReplaceLastOccurrence_ReplacementIsEmpty_RemovesLastOccurrence()
    {
        var result = "text-sm text-sm".ReplaceLastOccurrence("text-sm", "");

        Assert.Equal("text-sm ", result);
    }

    [Fact]
    public void ReplaceLastOccurrence_ThreeOccurrences_ReplacesOnlyThird()
    {
        var result = "a-a-a".ReplaceLastOccurrence("a", "X");

        Assert.Equal("a-a-X", result);
    }

    [Fact]
    public void ReplaceLastOccurrence_ColorStem_CorrectlyReplacesColorPart()
    {
        // Simulates the border-border fix: color = "border", stem = text.ReplaceLastOccurrence("border", "{0}")
        // text = "border-border", expected stem = "border-{0}"
        var text = "border-border";
        var color = "border";

        var stem = text.ReplaceLastOccurrence(color, "{0}");

        Assert.Equal("border-{0}", stem);
    }

    #endregion

    #region GetClosestString

    [Fact]
    public void GetClosestString_ExactMatch_ReturnsThatString()
    {
        var candidates = new[] { "text-red-500", "text-blue-500", "bg-white" };
        var result = "text-red-500".GetClosestString(candidates);

        Assert.Equal("text-red-500", result);
    }

    [Fact]
    public void GetClosestString_OneEditAway_ReturnsClosest()
    {
        var candidates = new[] { "text-red-500", "text-blue-500", "bg-white" };
        var result = "text-red-50".GetClosestString(candidates);

        Assert.Equal("text-red-500", result);
    }

    [Fact]
    public void GetClosestString_EmptyCandidates_ReturnsNull()
    {
        var result = "text-red-500".GetClosestString([]);

        Assert.Null(result);
    }

    [Fact]
    public void GetClosestString_SingleCandidate_ReturnsThatCandidate()
    {
        var result = "anything".GetClosestString(["bg-blue-500"]);

        Assert.Equal("bg-blue-500", result);
    }

    [Fact]
    public void GetClosestString_CaseInsensitiveMatching_PrefersLowerDistanceCaseInsensitive()
    {
        var candidates = new[] { "TEXT-RED-500", "text-red-500" };
        // "text-red-500" should be closer because lowercase matches lowercase
        var result = "text-red-500".GetClosestString(candidates);

        Assert.Equal("text-red-500", result);
    }

    #endregion

    #region JoinWithCommasAndAnd

    [Fact]
    public void JoinWithCommasAndAnd_EmptyList_ReturnsEmpty()
    {
        var result = Array.Empty<string>().JoinWithCommasAndAnd();

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void JoinWithCommasAndAnd_SingleItem_ReturnsThatItem()
    {
        var result = new[] { "foo" }.JoinWithCommasAndAnd();

        Assert.Equal("foo", result);
    }

    [Fact]
    public void JoinWithCommasAndAnd_TwoItems_JoinsWithAnd()
    {
        var result = new[] { "foo", "bar" }.JoinWithCommasAndAnd();

        Assert.Equal("foo and bar", result);
    }

    [Fact]
    public void JoinWithCommasAndAnd_ThreeItems_JoinsWithCommasAndAnd()
    {
        var result = new[] { "a", "b", "c" }.JoinWithCommasAndAnd();

        Assert.Equal("a, b and c", result);
    }

    [Fact]
    public void JoinWithCommasAndAnd_FourItems_JoinsAllWithCommasAndAnd()
    {
        var result = new[] { "a", "b", "c", "d" }.JoinWithCommasAndAnd();

        Assert.Equal("a, b, c and d", result);
    }

    [Fact]
    public void JoinWithCommasAndAnd_TypicalErrorMessageItems_FormatsCorrectly()
    {
        var items = new[] { "deprecatedAtRule", "usedBlocklistClass", "invalidSource" };
        var result = items.JoinWithCommasAndAnd();

        Assert.Equal("deprecatedAtRule, usedBlocklistClass and invalidSource", result);
    }

    #endregion
}