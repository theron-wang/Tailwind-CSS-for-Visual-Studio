using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

[Collection("Non-Parallel Tests")]
public class IntelliSenseAndDetectionIntegrationTests : IDisposable
{
    private readonly Func<
        Task<TailwindCSSIntellisense.Settings.TailwindSettings>
    >? _originalSettingsDelegate;

    public IntelliSenseAndDetectionIntegrationTests()
    {
        _originalSettingsDelegate = ClassRegexHelper.GetTailwindSettings;
        ClassRegexHelper.GetTailwindSettings = null;
    }

    public void Dispose()
    {
        ClassRegexHelper.GetTailwindSettings = _originalSettingsDelegate;
    }

    [Fact]
    public void ClassDetectionAndFiltering_HandlesMixedLanguagesAndBlocklist()
    {
        var values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V3,
            Blocklist = ["text-red-500"],
        };

        const string html = "<div class=\"text-red-500 p-4\"></div>";
        const string jsx = "<button className=\"hover:text-blue-500 text-red-500\"></button>";

        var htmlTokens = ClassRegexHelper
            .GetClassesNormal(html)
            .SelectMany(m =>
                ClassRegexHelper.SplitNonRazorClasses(ClassRegexHelper.GetClassTextGroup(m).Value)
            )
            .Select(m => m.Value);

        var jsTokens = ClassRegexHelper
            .GetClassesJavaScript(jsx)
            .SelectMany(m =>
                ClassRegexHelper.SplitNonRazorClasses(ClassRegexHelper.GetClassTextGroup(m).Value)
            )
            .Select(m => m.Value);

        var allowed = htmlTokens.Concat(jsTokens).Where(values.IsClassAllowed).ToList();

        Assert.Contains("p-4", allowed);
        Assert.Contains("hover:text-blue-500", allowed);
        Assert.DoesNotContain("text-red-500", allowed);
    }

    [Fact]
    public void KnownModifierEligibility_UsesCssVariablesForLineHeightSuggestions()
    {
        var values = new ProjectCompletionValues { CssVariables = { ["--text-sm"] = "14px" } };

        Assert.True(KnownModifiers.IsEligibleForLineHeightModifier("text-sm", values));
        Assert.False(KnownModifiers.IsEligibleForLineHeightModifier("text-shadow-md", values));
    }
}
