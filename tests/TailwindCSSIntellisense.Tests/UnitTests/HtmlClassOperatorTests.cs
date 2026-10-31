using System.Reflection;
using TailwindCSSIntellisense.ClassOperations.Operators;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

[Collection("Non-Parallel Tests")]
public class HtmlClassOperatorTests : IDisposable
{
    private readonly Func<TailwindSettings?>? _originalSettingsDelegate;

    public HtmlClassOperatorTests()
    {
        _originalSettingsDelegate = ClassRegexHelper.GetCachedTailwindSettings;
        ClassRegexHelper.GetCachedTailwindSettings = null;
    }

    [Theory]
    [InlineData(
        """<div :class="open ? 'text-red-500 p-4' : 'font-bold p-4'"></div>""",
        """<div :class="open ? 'p-4 text-red-500' : 'p-4 font-bold'"></div>"""
    )]
    public async Task RunOperationAsync_SortsNestedClassLiteralsAndPreservesAlpineExpression(
        string input,
        string expected
    )
    {
        var classOperator = new TestHtmlClassOperator();
        typeof(ClassOperator<TestSortOperation>)
            .GetField("_operation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(classOperator, new TestSortOperation());

        var result = await classOperator.RunOperationAsync("index.html", input);

        Assert.Equal(expected, result);
    }

    public void Dispose()
    {
        ClassRegexHelper.GetCachedTailwindSettings = _originalSettingsDelegate;
    }

    private sealed class TestHtmlClassOperator : HtmlClassOperator<TestSortOperation> { }

    // Keep these tests focused on extracting and rebuilding HTML expressions.
    // The Tailwind ordering rules are covered separately in SorterTests.
    private sealed class TestSortOperation : IClassOperation
    {
        public Task<IEnumerable<string>> OperateAsync(IEnumerable<string> classes, string filePath)
        {
            var order = new Dictionary<string, int>
            {
                ["p-4"] = 0,
                ["text-red-500"] = 1,
                ["font-bold"] = 2,
            };
            return Task.FromResult<IEnumerable<string>>(
                classes.OrderBy(className => order.GetValueOrDefault(className, -1)).ToArray()
            );
        }
    }
}
