using System.Collections;
using System.Globalization;
using System.Reflection;
using TailwindCSSIntellisense.ClassOperations.Operators;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Configuration.Descriptions;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class QodanaRegressionTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    [InlineData(0, false)]
    public async Task ConfigScanner_FindsConfigWithinTheFirstFifteenLines(int line, bool expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cssPath = Path.Combine(directory, "input.css");
            var configPath = Path.Combine(directory, "custom.config.js");
            await File.WriteAllTextAsync(configPath, "module.exports = {};");
            await File.WriteAllLinesAsync(
                cssPath,
                line == 0
                    ? []
                    : Enumerable
                        .Repeat("/* comment */", line - 1)
                        .Append("@config './custom.config.js';")
            );
            var finder = new FileFinder();
            finder.CssFiles.Add(cssPath);
            var scanner = new ConfigFileScanner { FileFinder = finder };

            var result = await scanner.TryFindConfigurationFileAsync();

            Assert.Equal(
                expected ? configPath : null,
                result is null ? null : Path.GetFullPath(result)
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("12px", "18px", "font-size: 12px;line-height: 18px;")]
    [InlineData(
        "12px",
        "{\"lineHeight\":\"18px\",\"letterSpacing\":\"1px\",\"fontWeight\":\"500\"}",
        "font-size: 12px;line-height: 18px;letter-spacing: 1px;font-weight: 500;"
    )]
    public void FontSizeDescription_EnumeratesInputOnce(
        string size,
        string metrics,
        string expected
    )
    {
        var input = new SinglePassEnumerable<string>([size, metrics]);

        Assert.Equal(expected, new FontSizeDescriptionGenerator().GetDescription(input));
        Assert.Equal(1, input.EnumerationCount);
    }

    [Fact]
    public async Task ClassOperator_EnumeratesOriginalClassesOnce()
    {
        var classes = new SinglePassEnumerable<string>(["p-2", "m-4"]);
        var classOperator = new SinglePassClassOperator(classes);
        typeof(ClassOperator<IdentityOperation>)
            .GetField("_operation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(classOperator, new IdentityOperation());

        Assert.Equal("p-2 m-4", await classOperator.RunOperationAsync("index.html", "p-2 m-4"));
        Assert.Equal(1, classes.EnumerationCount);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void ReplaceLastOccurrence_UsesLiteralOrdinalMatching(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(
                "changed p\u00AD-2",
                "p-2 p\u00AD-2".ReplaceLastOccurrence("p-2", "changed")
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ContainingDirectory_ResolvesRelativeFilePaths()
    {
        Assert.Equal(
            Directory.GetCurrentDirectory(),
            PathHelpers.GetContainingDirectory("input.css")
        );
        Assert.Equal(
            Path.Combine(Directory.GetCurrentDirectory(), "styles"),
            PathHelpers.GetContainingDirectory(Path.Combine("styles", "input.css"))
        );
    }

    [Fact]
    public void ContainingDirectory_RejectsPathsWithoutAParent()
    {
        Assert.Throws<ArgumentException>(() =>
            PathHelpers.GetContainingDirectory(Path.GetPathRoot(Directory.GetCurrentDirectory())!)
        );
    }

    private sealed class SinglePassEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            if (++EnumerationCount != 1)
            {
                throw new InvalidOperationException("The input was enumerated more than once.");
            }
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SinglePassClassOperator(IEnumerable<string> classes)
        : ClassOperator<IdentityOperation>
    {
        public override string[] Handled => [".html"];

        protected override async IAsyncEnumerable<ClassOpToken> GetSegmentsAsync(
            string filePath,
            string input
        )
        {
            await Task.CompletedTask;
            yield return new CustomSplitClassOpToken(classes, input)
            {
                Type = ClassOpTokenType.Class,
            };
        }
    }

    private sealed class IdentityOperation : IClassOperation
    {
        public Task<IEnumerable<string>> OperateAsync(
            IEnumerable<string> classes,
            string filePath
        ) => Task.FromResult<IEnumerable<string>>(classes.ToArray());
    }
}
