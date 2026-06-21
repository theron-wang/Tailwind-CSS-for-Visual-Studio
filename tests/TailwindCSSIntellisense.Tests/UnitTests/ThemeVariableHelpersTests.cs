using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class ThemeVariableHelpersTests
{
    #region GetCssVariableNamespace

    [Theory]
    [InlineData("--color-red-500", "--color-")]
    [InlineData("--color-blue-100", "--color-")]
    [InlineData("--font-weight-bold", "--font-weight-")]
    [InlineData("--font-sans", "--font-")]
    [InlineData("--text-sm", "--text-")]
    [InlineData("--text-base", "--text-")]
    [InlineData("--tracking-wide", "--tracking-")]
    [InlineData("--leading-relaxed", "--leading-")]
    [InlineData("--breakpoint-md", "--breakpoint-")]
    [InlineData("--container-sm", "--container-")]
    [InlineData("--spacing-4", "--spacing-")]
    [InlineData("--radius-lg", "--radius-")]
    [InlineData("--shadow-md", "--shadow-")]
    [InlineData("--inset-shadow-sm", "--inset-shadow-")]
    [InlineData("--drop-shadow-lg", "--drop-shadow-")]
    [InlineData("--blur-sm", "--blur-")]
    [InlineData("--perspective-near", "--perspective-")]
    [InlineData("--aspect-video", "--aspect-")]
    [InlineData("--ease-in-out", "--ease-")]
    [InlineData("--animate-spin", "--animate-")]
    public void GetCssVariableNamespace_KnownPrefixes_ReturnsCorrectNamespace(
        string variable,
        string expected
    )
    {
        var result = ThemeVariableHelpers.GetCssVariableNamespace(variable, TailwindVersion.V4);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetCssVariableNamespace_TextShadow_BelowV4_1_ReturnsFontNamespace()
    {
        // --text-shadow starts with --text- so before V4_1 it should fall through to --text-
        var result = ThemeVariableHelpers.GetCssVariableNamespace(
            "--text-shadow-sm",
            TailwindVersion.V4
        );

        Assert.Equal("--text-", result);
    }

    [Fact]
    public void GetCssVariableNamespace_TextShadow_AtV4_1_ReturnsTextShadowNamespace()
    {
        var result = ThemeVariableHelpers.GetCssVariableNamespace(
            "--text-shadow-sm",
            TailwindVersion.V4_1
        );

        Assert.Equal("--text-shadow-", result);
    }

    [Fact]
    public void GetCssVariableNamespace_TextShadow_AtV4_2_ReturnsTextShadowNamespace()
    {
        var result = ThemeVariableHelpers.GetCssVariableNamespace(
            "--text-shadow-lg",
            TailwindVersion.V4_2
        );

        Assert.Equal("--text-shadow-", result);
    }

    [Fact]
    public void GetCssVariableNamespace_UnknownVariable_ReturnsNull()
    {
        var result = ThemeVariableHelpers.GetCssVariableNamespace(
            "--custom-unknown-var",
            TailwindVersion.V4
        );

        Assert.Null(result);
    }

    [Fact]
    public void GetCssVariableNamespace_EmptyString_ReturnsNull()
    {
        var result = ThemeVariableHelpers.GetCssVariableNamespace("", TailwindVersion.V4);

        Assert.Null(result);
    }

    [Fact]
    public void GetCssVariableNamespace_FontWeightTakesPrecedenceOverFont()
    {
        // --font-weight- should match before --font-
        var result = ThemeVariableHelpers.GetCssVariableNamespace(
            "--font-weight-500",
            TailwindVersion.V4
        );

        Assert.Equal("--font-weight-", result);
    }

    [Fact]
    public void GetCssVariableNamespace_InsetShadowTakesPrecedenceOverShadow()
    {
        // --inset-shadow- should NOT match --shadow-
        var shadowResult = ThemeVariableHelpers.GetCssVariableNamespace(
            "--shadow-lg",
            TailwindVersion.V4
        );
        var insetShadowResult = ThemeVariableHelpers.GetCssVariableNamespace(
            "--inset-shadow-lg",
            TailwindVersion.V4
        );

        Assert.Equal("--shadow-", shadowResult);
        Assert.Equal("--inset-shadow-", insetShadowResult);
    }

    #endregion

    #region GetConfigurationClassStemFromCssVariable

    [Theory]
    [InlineData("--color-red-500", "colors")]
    [InlineData("--font-weight-bold", "fontWeight")]
    [InlineData("--font-sans", "fontFamily")]
    [InlineData("--text-sm", "fontSize")]
    [InlineData("--tracking-wide", "letterSpacing")]
    [InlineData("--leading-relaxed", "lineHeight")]
    [InlineData("--breakpoint-md", "screens")]
    [InlineData("--container-sm", "v4-container")]
    [InlineData("--spacing-4", "spacing")]
    [InlineData("--radius-lg", "borderRadius")]
    [InlineData("--shadow-md", "boxShadow")]
    [InlineData("--inset-shadow-sm", "v4-insetShadow")]
    [InlineData("--drop-shadow-lg", "dropShadow")]
    [InlineData("--blur-sm", "blur")]
    [InlineData("--perspective-near", "v4-perspective")]
    [InlineData("--aspect-video", "aspectRatio")]
    [InlineData("--ease-in-out", "transitionTimingFunction")]
    [InlineData("--animate-spin", "animation")]
    public void GetConfigurationClassStemFromCssVariable_KnownPrefixes_ReturnsCorrectStem(
        string variable,
        string expectedStem
    )
    {
        var result = ThemeVariableHelpers.GetConfigurationClassStemFromCssVariable(
            variable,
            TailwindVersion.V4
        );

        Assert.Equal(expectedStem, result);
    }

    [Fact]
    public void GetConfigurationClassStemFromCssVariable_TextShadow_BelowV4_1_ReturnsFontSize()
    {
        // --text-shadow starts with --text-, falls through to fontSize before V4_1
        var result = ThemeVariableHelpers.GetConfigurationClassStemFromCssVariable(
            "--text-shadow-sm",
            TailwindVersion.V4
        );

        Assert.Equal("fontSize", result);
    }

    [Fact]
    public void GetConfigurationClassStemFromCssVariable_TextShadow_AtV4_1_ReturnsV4_1TextShadow()
    {
        var result = ThemeVariableHelpers.GetConfigurationClassStemFromCssVariable(
            "--text-shadow-sm",
            TailwindVersion.V4_1
        );

        Assert.Equal("v4_1-text-shadow", result);
    }

    [Fact]
    public void GetConfigurationClassStemFromCssVariable_UnknownVariable_ReturnsNull()
    {
        var result = ThemeVariableHelpers.GetConfigurationClassStemFromCssVariable(
            "--my-custom-variable",
            TailwindVersion.V4
        );

        Assert.Null(result);
    }

    [Fact]
    public void GetConfigurationClassStemFromCssVariable_EmptyString_ReturnsNull()
    {
        var result = ThemeVariableHelpers.GetConfigurationClassStemFromCssVariable(
            "",
            TailwindVersion.V4
        );

        Assert.Null(result);
    }

    [Fact]
    public void GetConfigurationClassStemFromCssVariable_FontWeightPrecedesFont()
    {
        var fontWeightResult = ThemeVariableHelpers.GetConfigurationClassStemFromCssVariable(
            "--font-weight-bold",
            TailwindVersion.V4
        );
        var fontFamilyResult = ThemeVariableHelpers.GetConfigurationClassStemFromCssVariable(
            "--font-serif",
            TailwindVersion.V4
        );

        Assert.Equal("fontWeight", fontWeightResult);
        Assert.Equal("fontFamily", fontFamilyResult);
    }

    #endregion

    #region FormatConfigurationPathFromCssVariable

    [Theory]
    [InlineData("--color-blue-500", "colors.blue.500")]
    [InlineData("--font-weight-bold", "fontWeight.bold")]
    [InlineData("--font-sans", "fontFamily.sans")]
    [InlineData("--text-sm", "fontSize.sm")]
    [InlineData("--tracking-wide", "letterSpacing.wide")]
    [InlineData("--leading-relaxed", "lineHeight.relaxed")]
    [InlineData("--breakpoint-md", "screens.md")]
    [InlineData("--spacing-4", "spacing.4")]
    [InlineData("--radius-lg", "borderRadius.lg")]
    [InlineData("--shadow-md", "boxShadow.md")]
    [InlineData("--inset-shadow-sm", "insetShadow.sm")]
    [InlineData("--drop-shadow-lg", "dropShadow.lg")]
    [InlineData("--blur-sm", "blur.sm")]
    [InlineData("--perspective-near", "perspective.near")]
    [InlineData("--aspect-video", "aspectRatio.video")]
    [InlineData("--ease-in-out", "transitionTimingFunction.in.out")]
    [InlineData("--animate-spin", "animation.spin")]
    public void FormatConfigurationPathFromCssVariable_KnownPrefixes_FormatsCorrectly(
        string variable,
        string expectedPath
    )
    {
        var result = ThemeVariableHelpers.FormatConfigurationPathFromCssVariable(
            variable,
            TailwindVersion.V4
        );

        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void FormatConfigurationPathFromCssVariable_TextShadow_AtV4_1_FormatsAsTextShadow()
    {
        var result = ThemeVariableHelpers.FormatConfigurationPathFromCssVariable(
            "--text-shadow-sm",
            TailwindVersion.V4_1
        );

        Assert.Equal("textShadow.sm", result);
    }

    [Fact]
    public void FormatConfigurationPathFromCssVariable_TextShadow_BelowV4_1_FormatsAsFontSize()
    {
        var result = ThemeVariableHelpers.FormatConfigurationPathFromCssVariable(
            "--text-shadow-sm",
            TailwindVersion.V4
        );

        Assert.Equal("fontSize.shadow.sm", result);
    }

    [Fact]
    public void FormatConfigurationPathFromCssVariable_UnknownVariable_ReturnsNull()
    {
        var result = ThemeVariableHelpers.FormatConfigurationPathFromCssVariable(
            "--unknown-custom-var",
            TailwindVersion.V4
        );

        Assert.Null(result);
    }

    [Fact]
    public void FormatConfigurationPathFromCssVariable_DashesInValuePartBecomesDots()
    {
        // --color-slate-blue-300 → colors.slate.blue.300
        var result = ThemeVariableHelpers.FormatConfigurationPathFromCssVariable(
            "--color-slate-blue-300",
            TailwindVersion.V4
        );

        Assert.Equal("colors.slate.blue.300", result);
    }

    [Fact]
    public void FormatConfigurationPathFromCssVariable_ContainerPrefix_FormatsCorrectly()
    {
        // --container-sm is v4-only
        var result = ThemeVariableHelpers.FormatConfigurationPathFromCssVariable(
            "--container-sm",
            TailwindVersion.V4
        );

        Assert.NotNull(result);
        Assert.Contains("sm", result);
    }

    #endregion

    #region GetCssVariableFromConfigurationClassStem

    [Theory]
    [InlineData("colors", "--color")]
    [InlineData("fontWeight", "--font-weight")]
    [InlineData("fontFamily", "--font")]
    [InlineData("textShadow", "--text-shadow")]
    [InlineData("fontSize", "--text")]
    [InlineData("letterSpacing", "--tracking")]
    [InlineData("lineHeight", "--leading")]
    [InlineData("screens", "--breakpoint")]
    [InlineData("spacing", "--spacing")]
    [InlineData("borderRadius", "--radius")]
    [InlineData("boxShadow", "--shadow")]
    [InlineData("insetShadow", "--inset-shadow")]
    [InlineData("dropShadow", "--drop-shadow")]
    [InlineData("blur", "--blur")]
    [InlineData("perspective", "--perspective")]
    [InlineData("aspectRatio", "--aspect")]
    [InlineData("transitionTimingFunction", "--ease")]
    [InlineData("animation", "--animate")]
    public void GetCssVariableFromConfigurationClassStem_KnownStems_ReturnsCssVariable(
        string stem,
        string expectedVariable
    )
    {
        var result = ThemeVariableHelpers.GetCssVariableFromConfigurationClassStem(stem);

        Assert.Equal(expectedVariable, result);
    }

    [Fact]
    public void GetCssVariableFromConfigurationClassStem_UnknownStem_ReturnsNull()
    {
        var result = ThemeVariableHelpers.GetCssVariableFromConfigurationClassStem("unknownStem");

        Assert.Null(result);
    }

    [Fact]
    public void GetCssVariableFromConfigurationClassStem_EmptyString_ReturnsNull()
    {
        var result = ThemeVariableHelpers.GetCssVariableFromConfigurationClassStem("");

        Assert.Null(result);
    }

    [Fact]
    public void GetCssVariableFromConfigurationClassStem_V4ContainerStem_ReturnsNull()
    {
        // "v4-container" is a GetConfigurationClassStemFromCssVariable output but not a GetCssVariableFromConfigurationClassStem input
        var result = ThemeVariableHelpers.GetCssVariableFromConfigurationClassStem("v4-container");

        Assert.Null(result);
    }

    #endregion

    #region Roundtrip consistency

    [Theory]
    [InlineData("--color-red-500", TailwindVersion.V4)]
    [InlineData("--font-weight-bold", TailwindVersion.V4)]
    [InlineData("--text-sm", TailwindVersion.V4)]
    [InlineData("--spacing-4", TailwindVersion.V4)]
    [InlineData("--radius-lg", TailwindVersion.V4)]
    public void GetNamespaceAndStem_BothReturnNonNullForSameInput(
        string variable,
        TailwindVersion version
    )
    {
        var ns = ThemeVariableHelpers.GetCssVariableNamespace(variable, version);
        var stem = ThemeVariableHelpers.GetConfigurationClassStemFromCssVariable(variable, version);

        Assert.NotNull(ns);
        Assert.NotNull(stem);
    }

    [Theory]
    [InlineData("--custom-var", TailwindVersion.V4)]
    [InlineData("--unknown", TailwindVersion.V4)]
    public void GetNamespaceAndStem_BothReturnNullForUnknownInput(
        string variable,
        TailwindVersion version
    )
    {
        var ns = ThemeVariableHelpers.GetCssVariableNamespace(variable, version);
        var stem = ThemeVariableHelpers.GetConfigurationClassStemFromCssVariable(variable, version);

        Assert.Null(ns);
        Assert.Null(stem);
    }

    #endregion
}
