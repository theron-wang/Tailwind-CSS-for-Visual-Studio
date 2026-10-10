using System.Reflection;
using TailwindCSSIntellisense.ClassOperations;
using TailwindCSSIntellisense.ClassOperations.Operators;
using TailwindCSSIntellisense.ClassOperations.Sorters;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

public class SortingIntegrationTests
{
    [Fact]
    public async Task OperateAsync_OrdersClassesByVariantAndClassOrder()
    {
        var sorter = CreateSorter();

        var sorted = await SortClassesAsync(
            sorter,
            "hover:font-bold text-red-500 p-4",
            "index.html"
        );

        Assert.Equal("p-4 text-red-500 hover:font-bold", sorted);
    }

    [Fact]
    public async Task Sort_UpdatesOnlyClassSegmentAndPreservesMarkup()
    {
        var sorter = CreateSorter();
        const string input = "<div class=\"text-red-500 p-4\">hello</div>";

        var htmlSorter = CreateOperator<HtmlSorter>(sorter);

        var sorted = await htmlSorter.RunOperationAsync("index.html", input);

        Assert.Equal("<div class=\"p-4 text-red-500\">hello</div>", sorted);
    }

    [Fact]
    public async Task HtmlSorter_SortsBothQuoteStylesAndPreservesOtherAttributes()
    {
        var sorter = CreateOperator<HtmlSorter>(CreateSorter());
        const string content =
            "<div class='text-red-500 p-4' data-x=\"y\" class=\"hover:font-bold p-4\"></div>";

        var sorted = await sorter.RunOperationAsync("index.html", content);

        Assert.Equal(
            "<div class='p-4 text-red-500' data-x=\"y\" class=\"p-4 hover:font-bold\"></div>",
            sorted
        );
    }

    [Fact]
    public async Task RazorSorter_KeepsRazorExpressionAndSortsNonRazorClasses()
    {
        var razorSorter = CreateRazorSorter();
        const string input =
            "<div class=\"text-red-500 @(isActive ? 'font-bold' : 'font-light') p-4\"></div>";

        var sorted = await razorSorter.RunOperationAsync("page.razor", input);

        Assert.Contains("@(isActive ? 'font-bold' : 'font-light')", sorted);
        Assert.Contains("class=\"p-4 ", sorted);
        Assert.EndsWith(" text-red-500\"></div>", sorted);
    }

    [Fact]
    public async Task RazorSorter_KeepsEscapedAtSignsAndMultilineIndentation()
    {
        var razorSorter = CreateRazorSorter();
        const string input = "<div class=\"text-red-500 @@container\n    p-4\"></div>";

        var sorted = await razorSorter.RunOperationAsync("page.razor", input);

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

        var sorter = CreateSorter(projectManager, classSortUtilities);

        Assert.Equal(
            "p-4 text-red-500",
            await SortClassesAsync(sorter, "text-red-500 p-4", "v3.html")
        );
        Assert.Equal(
            "text-red-500 p-4",
            await SortClassesAsync(sorter, "text-red-500 p-4", "v4.html")
        );
    }

    private static Sorter CreateSorter()
    {
        var (projectManager, classSortUtilities) = CreateSharedDependencies();
        return CreateSorter(projectManager, classSortUtilities);
    }

    private static Sorter CreateSorter(
        ProjectConfigurationManager projectManager,
        ClassSortUtilities classSortUtilities
    )
    {
        var sorter = new Sorter();
        typeof(Sorter)
            .GetField(
                "_projectConfigurationManager",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .SetValue(sorter, projectManager);
        typeof(Sorter)
            .GetField("_classSortUtilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(sorter, classSortUtilities);
        return sorter;
    }

    private static TOperator CreateOperator<TOperator>(Sorter sorter)
        where TOperator : ClassOperator<Sorter>, new()
    {
        var classOperator = new TOperator();
        typeof(ClassOperator<Sorter>)
            .GetField("_operation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(classOperator, sorter);
        return classOperator;
    }

    private static RazorSorter CreateRazorSorter()
    {
        return CreateOperator<RazorSorter>(CreateSorter());
    }

    private static async Task<string> SortClassesAsync(
        Sorter sorter,
        string classText,
        string filePath
    )
    {
        var classes = classText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", await sorter.OperateAsync(classes, filePath));
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
}
