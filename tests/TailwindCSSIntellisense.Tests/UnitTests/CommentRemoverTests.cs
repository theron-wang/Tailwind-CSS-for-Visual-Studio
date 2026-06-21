using TailwindCSSIntellisense.Helpers;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class CommentRemoverTests
{
    [Fact]
    public void StripCssComments_NoComments_ReturnsOriginal()
    {
        const string css = ".foo { color: red; }";

        var result = CommentRemover.StripCssComments(css);

        Assert.Equal(css, result);
    }

    [Fact]
    public void StripCssComments_SingleBlockComment_RemovesComment()
    {
        const string css = "/* this is a comment */ .foo { color: red; }";

        var result = CommentRemover.StripCssComments(css);

        // Comment characters become spaces; rest is preserved
        Assert.DoesNotContain("this is a comment", result);
        Assert.Contains(".foo { color: red; }", result);
    }

    [Fact]
    public void StripCssComments_PreservesLength()
    {
        const string css = "/* comment */ .foo { color: red; }";

        var result = CommentRemover.StripCssComments(css);

        Assert.Equal(css.Length, result.Length);
    }

    [Fact]
    public void StripCssComments_PreservesNewlinesInsideComment()
    {
        const string css = "/* line1\nline2 */ .foo { }";

        var result = CommentRemover.StripCssComments(css);

        // Newlines inside comments should be preserved (not replaced with spaces)
        Assert.Equal('\n', result[css.IndexOf('\n')]);
        Assert.Equal(css.Length, result.Length);
    }

    [Fact]
    public void StripCssComments_MultipleComments_RemovesAll()
    {
        const string css = "/* c1 */ .a { } /* c2 */ .b { }";

        var result = CommentRemover.StripCssComments(css);

        Assert.DoesNotContain("c1", result);
        Assert.DoesNotContain("c2", result);
        Assert.Contains(".a { }", result);
        Assert.Contains(".b { }", result);
    }

    [Fact]
    public void StripCssComments_CommentInsideDoubleQuotedString_PreservesComment()
    {
        const string css = ".foo { content: \"/* not a comment */\"; }";

        var result = CommentRemover.StripCssComments(css);

        Assert.Contains("/* not a comment */", result);
    }

    [Fact]
    public void StripCssComments_CommentInsideSingleQuotedString_PreservesComment()
    {
        const string css = ".foo { content: '/* not a comment */'; }";

        var result = CommentRemover.StripCssComments(css);

        Assert.Contains("/* not a comment */", result);
    }

    [Fact]
    public void StripCssComments_EmptyString_ReturnsEmpty()
    {
        var result = CommentRemover.StripCssComments("");

        Assert.Equal("", result);
    }

    [Fact]
    public void StripCssComments_OnlyComment_ReturnsSpaces()
    {
        const string css = "/* hello */";

        var result = CommentRemover.StripCssComments(css);

        Assert.Equal(css.Length, result.Length);
        // All non-newline characters become spaces
        Assert.All(result.ToCharArray(), c => Assert.Equal(' ', c));
    }

    [Fact]
    public void StripCssComments_EscapedQuoteInsideString_HandledCorrectly()
    {
        const string css = ".foo { content: \"he said \\\"hello\\\"\"; } /* comment */";

        var result = CommentRemover.StripCssComments(css);

        // Comment should be stripped, string should be intact
        Assert.DoesNotContain("comment", result);
        Assert.Contains("he said", result);
    }

    [Fact]
    public void StripCssComments_CssApplyWithComment_StripsCommentBeforeRegex()
    {
        // Simulates the use case in ClassRegexHelper.GetClassesCss
        const string css = "@apply text-red-500 /* comment */ font-bold;";

        var result = CommentRemover.StripCssComments(css);

        Assert.DoesNotContain("comment", result);
        Assert.Contains("@apply", result);
        Assert.Contains("text-red-500", result);
        Assert.Contains("font-bold", result);
    }

    [Fact]
    public void StripCssComments_CommentAtEndWithoutClosingSlash_HandlesGracefully()
    {
        // Unclosed comment - should not throw, just consume to end
        const string css = ".foo { } /* unclosed";

        var result = CommentRemover.StripCssComments(css);

        // Should not throw and length should be preserved
        Assert.Equal(css.Length, result.Length);
    }

    [Fact]
    public void StripCssComments_CommentWithAsteriskInsidePrecedingClose_HandledCorrectly()
    {
        // /* foo * / bar */ - the * / in the middle is NOT a closer
        const string css = "/* foo * bar */ .a { }";

        var result = CommentRemover.StripCssComments(css);

        Assert.DoesNotContain("foo", result);
        Assert.DoesNotContain("bar", result);
        Assert.Contains(".a { }", result);
    }

    [Fact]
    public void StripCssComments_IndicesOfNonCommentTextPreserved()
    {
        // Verify that the character at a known position outside a comment is unchanged
        const string css = "/* xyz */ .a { color: red; }";
        int expectedDotIndex = css.IndexOf('.');

        var result = CommentRemover.StripCssComments(css);

        Assert.Equal('.', result[expectedDotIndex]);
        Assert.Equal(css.Length, result.Length);
    }
}