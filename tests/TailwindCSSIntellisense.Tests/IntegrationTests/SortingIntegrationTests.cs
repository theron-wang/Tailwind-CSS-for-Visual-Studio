using TailwindCSSIntellisense.ClassSort;
using TailwindCSSIntellisense.ClassSort.Sorters;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

public class SortingIntegrationTests
{
    [Fact]
    public async Task SortSegment_OrdersClassesByVariantAndClassOrder()
    {
        var sorter = CreateTestSorter();

        var sorted = await sorter.SortSegmentPublic(
            "hover:font-bold text-red-500 p-4",
            "index.html"
        );

        Assert.Equal("p-4 text-red-500 hover:font-bold", sorted);
    }

    [Fact]
    public async Task Sort_UpdatesOnlyClassSegmentAndPreservesMarkup()
    {
        var sorter = CreateTestSorter();
        const string input = "<div class=\"text-red-500 p-4\">hello</div>";

        var sorted = await sorter.SortContent("index.html", input);

        Assert.Equal("<div class=\"p-4 text-red-500\">hello</div>", sorted);
    }

    [Fact]
    public async Task GetNextIndexOfClass_PicksNearestQuoteVariant()
    {
        var sorter = CreateTestSorter();
        const string content = "<div class='a b' data-x=\"y\" class=\"c d\"></div>";

        var (index, terminator) = sorter.GetNextIndex(content, 0);
        var (secondIndex, secondTerminator) = sorter.GetNextIndex(content, index + 1);

        Assert.Equal('\'', terminator);
        Assert.Equal('"', secondTerminator);
        Assert.True(secondIndex > index);
    }

    [Fact]
    public async Task RazorSorter_KeepsRazorExpressionAndSortsNonRazorClasses()
    {
        var razorSorter = CreateRazorSorter();
        const string input =
            "<div class=\"text-red-500 @(isActive ? 'font-bold' : 'font-light') p-4\"></div>";

        var sorted = await razorSorter.SortAsync("page.razor", input);

        Assert.Contains("@(isActive ? 'font-bold' : 'font-light')", sorted);
        Assert.Contains("class=\"p-4 ", sorted);
        Assert.EndsWith(" text-red-500\"></div>", sorted);
    }

    [Fact]
    public async Task RazorSorter_KeepsEscapedAtSignsAndMultilineIndentation()
    {
        var razorSorter = CreateRazorSorter();
        const string input = "<div class=\"text-red-500 @@container\n    p-4\"></div>";

        var sorted = await razorSorter.SortAsync("page.razor", input);

        Assert.Contains("@@container", sorted);
        Assert.Contains("\n    @@container", sorted);
        Assert.StartsWith("<div class=\"p-4 text-red-500", sorted);
    }

    [Fact]
    public async Task Sorter_UsesVersionSpecificOrderMappings()
    {
        var v3Values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V3,
            SpacingMapper = { ["4"] = "1rem" },
            ColorMapper = { ["red-500"] = "#f00" },
        };
        var v4Values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V4,
            SpacingMapper = { ["4"] = "1rem" },
            ColorMapper = { ["red-500"] = "#f00" },
        };

        var projectManager = new ProjectConfigurationManager();
        projectManager.Seed("v3.html", v3Values);
        projectManager.Seed("v4.html", v4Values);
        projectManager.SeedDefault(v3Values);

        var classSortUtilities = new ClassSortUtilities(
            classOrders: new Dictionary<TailwindVersion, Dictionary<string, int>>
            {
                [TailwindVersion.V3] = new() { ["p-{s}"] = 1, ["text-{c}"] = 2 },
                [TailwindVersion.V4] = new() { ["text-{c}"] = 1, ["p-{s}"] = 2 },
            },
            variantOrders: new Dictionary<TailwindVersion, Dictionary<string, int>>
            {
                [TailwindVersion.V3] = new(),
                [TailwindVersion.V4] = new(),
            }
        );

        var sorter = new TestSorter
        {
            ProjectConfigurationManager = projectManager,
            ClassSortUtilities = classSortUtilities,
        };

        Assert.Equal(
            "p-4 text-red-500",
            await sorter.SortSegmentPublic("text-red-500 p-4", "v3.html")
        );
        Assert.Equal(
            "text-red-500 p-4",
            await sorter.SortSegmentPublic("text-red-500 p-4", "v4.html")
        );
    }

    private static TestSorter CreateTestSorter()
    {
        var (projectManager, classSortUtilities) = CreateSharedDependencies();

        return new TestSorter
        {
            ProjectConfigurationManager = projectManager,
            ClassSortUtilities = classSortUtilities,
        };
    }

    private static RazorSorter CreateRazorSorter()
    {
        var (projectManager, classSortUtilities) = CreateSharedDependencies();

        return new RazorSorter
        {
            ProjectConfigurationManager = projectManager,
            ClassSortUtilities = classSortUtilities,
        };
    }

    private static (
        ProjectConfigurationManager projectManager,
        ClassSortUtilities classSortUtilities
    ) CreateSharedDependencies()
    {
        var values = new ProjectCompletionValues
        {
            Version = TailwindVersion.V3,
            ColorMapper = { ["red-500"] = "#f00" },
            SpacingMapper = { ["4"] = "1rem" },
            Breakpoints = { ["sm"] = "640px" },
        };

        var projectManager = new ProjectConfigurationManager();
        projectManager.SeedDefault(values);
        projectManager.Seed("index.html", values);
        projectManager.Seed("page.razor", values);

        var classSortUtilities = new ClassSortUtilities(
            classOrders: new Dictionary<TailwindVersion, Dictionary<string, int>>
            {
                [TailwindVersion.V3] = new Dictionary<string, int>
                {
                    ["p-{s}"] = 1,
                    ["text-{c}"] = 2,
                    ["font-bold"] = 3,
                    ["font-light"] = 4,
                    ["@container"] = 5,
                },
            },
            variantOrders: new Dictionary<TailwindVersion, Dictionary<string, int>>
            {
                [TailwindVersion.V3] = new Dictionary<string, int> { ["hover"] = 10, ["sm"] = 20 },
            }
        );

        return (projectManager, classSortUtilities);
    }

    private sealed class TestSorter : Sorter
    {
        public override string[] Handled => [".html"];

        public Task<string> SortSegmentPublic(string classText, string filePath)
        {
            return SortSegmentAsync(classText, filePath);
        }

        public Task<string> SortContent(string filePath, string input)
        {
            return SortAsync(filePath, input);
        }

        public (int index, char terminator) GetNextIndex(string input, int startIndex)
        {
            return GetNextIndexOfClass(input, startIndex);
        }

        protected override async IAsyncEnumerable<string> GetSegmentsAsync(
            string filePath,
            string input
        )
        {
            const string marker = "class=\"";
            var classStart = input.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (classStart < 0)
            {
                yield return input;
                yield break;
            }

            var classValueStart = classStart + marker.Length;
            var classValueEnd = input.IndexOf('"', classValueStart);
            if (classValueEnd < 0)
            {
                yield return input;
                yield break;
            }

            yield return input[..classValueStart];
            yield return await SortSegmentAsync(input[classValueStart..classValueEnd], filePath);
            yield return input[classValueEnd..];
        }
    }
}
