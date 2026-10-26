using System.Reflection;
using TailwindCSSIntellisense.ClassOperations;
using TailwindCSSIntellisense.ClassOperations.Sorters;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class SorterTests
{
    private const string FilePath = "index.html";

    [Theory]
    [InlineData("")]
    [InlineData("p-4")]
    [InlineData("block p-4 text-red-500")]
    public async Task OperateAsync_LeavesEmptySingleAndSortedInputsUnchanged(string input)
    {
        Assert.Equal(input, await SortAsync(CreateValues(), input));
    }

    [Theory]
    [InlineData(TailwindVersion.V3)]
    [InlineData(TailwindVersion.V4)]
    public async Task OperateAsync_UsesClassOrderAndPreservesDuplicates(TailwindVersion version)
    {
        Assert.Equal(
            "block p-4 p-4 text-red-500 font-bold",
            await SortAsync(CreateValues(version), "font-bold text-red-500 p-4 block p-4")
        );
    }

    [Theory]
    [InlineData(TailwindVersion.V3)]
    [InlineData(TailwindVersion.V4)]
    public async Task OperateAsync_KeepsUnknownClassesFirstAndEqualOrderStable(
        TailwindVersion version
    )
    {
        Assert.Equal(
            "custom-z custom-a p-8 p-4 text-red-500",
            await SortAsync(CreateValues(version), "text-red-500 custom-z p-8 custom-a p-4")
        );
    }

    [Theory]
    [InlineData(TailwindVersion.V3, "!font-bold p-4", "p-4 !font-bold")]
    [InlineData(TailwindVersion.V4, "font-bold! p-4", "p-4 font-bold!")]
    [InlineData(
        TailwindVersion.V3,
        "text-red-500/50 -m-4 p-[2rem]",
        "p-[2rem] -m-4 text-red-500/50"
    )]
    [InlineData(TailwindVersion.V4, "text-[#fff] p-[2rem]", "p-[2rem] text-[#fff]")]
    [InlineData(TailwindVersion.V4, "text-red-500 p-2.5", "p-2.5 text-red-500")]
    [InlineData(TailwindVersion.V4, "text-red-500 p-px", "p-px text-red-500")]
    [InlineData(TailwindVersion.V4, "flex-2 w-1/2", "w-1/2 flex-2")]
    [InlineData(TailwindVersion.V4, "opacity-50% flex-2", "flex-2 opacity-50%")]
    [InlineData(
        TailwindVersion.V4,
        "translate-x-(--offset) flex-2",
        "flex-2 translate-x-(--offset)"
    )]
    public async Task OperateAsync_RecognizesModifiersAndDynamicValues(
        TailwindVersion version,
        string input,
        string expected
    )
    {
        Assert.Equal(expected, await SortAsync(CreateValues(version), input));
    }

    [Theory]
    [InlineData(TailwindVersion.V3)]
    [InlineData(TailwindVersion.V4)]
    public async Task OperateAsync_OrdersVariantsBeforeClassesAndUsesHighestStackedVariant(
        TailwindVersion version
    )
    {
        Assert.Equal(
            "p-4 hover:p-4 hover:font-bold focus:p-4 mystery:p-4 hover:focus:p-4 focus:hover:font-bold",
            await SortAsync(
                CreateValues(version),
                "focus:hover:font-bold mystery:p-4 focus:p-4 hover:font-bold hover:focus:p-4 hover:p-4 p-4"
            )
        );
    }

    [Theory]
    [InlineData(TailwindVersion.V3, "", "")]
    [InlineData(TailwindVersion.V4, "", "")]
    [InlineData(TailwindVersion.V4, "tw:", "tw:")]
    public async Task OperateAsync_OrdersBreakpointsBySize(
        TailwindVersion version,
        string prefix,
        string classPrefix
    )
    {
        var values = CreateValues(version);
        values.Prefix = prefix;

        Assert.Equal(
            $"{classPrefix}sm:p-4 {classPrefix}lg:p-4",
            await SortAsync(values, $"{classPrefix}lg:p-4 {classPrefix}sm:p-4")
        );
    }

    [Fact]
    public async Task OperateAsync_V3KeepsUnprefixedClassesFirstAndSortsPrefixedClasses()
    {
        var values = CreateValues();
        values.Prefix = "tw-";

        Assert.Equal(
            "font-bold tw-p-4 -tw-m-4 tw-text-red-500",
            await SortAsync(values, "tw-text-red-500 -tw-m-4 font-bold tw-p-4")
        );
    }

    [Fact]
    public async Task OperateAsync_V3RecognizesCustomSpacingAndColors()
    {
        var values = CreateValues();
        values.CustomSpacingMappers["p-{0}"] = new() { ["gutter"] = "1.5rem" };
        values.CustomColorMappers["text-{0}"] = new() { ["brand-primary"] = "#123456" };

        Assert.Equal(
            "p-gutter text-brand-primary font-bold",
            await SortAsync(values, "font-bold text-brand-primary p-gutter")
        );
    }

    [Theory]
    [InlineData(TailwindVersion.V3)]
    [InlineData(TailwindVersion.V4)]
    public async Task OperateAsync_TreatsBlocklistedClassesAsUnknown(TailwindVersion version)
    {
        var values = CreateValues(version);
        values.Blocklist.Add("font-bold");

        Assert.Equal("font-bold p-4", await SortAsync(values, "p-4 font-bold"));
    }

    [Fact]
    public async Task OperateAsync_V4OrdersContainerVariantsBySize()
    {
        Assert.Equal(
            "@sm:p-4 @lg:p-4",
            await SortAsync(CreateValues(TailwindVersion.V4), "@lg:p-4 @sm:p-4")
        );
    }

    private static ProjectCompletionValues CreateValues(
        TailwindVersion version = TailwindVersion.V3
    )
    {
        return new ProjectCompletionValues
        {
            Version = version,
            SpacingMapper = { ["4"] = "1rem", ["8"] = "2rem" },
            ColorMapper = { ["red-500"] = "#f00" },
            // Deliberately insert larger sizes first to catch reliance on dictionary order.
            Breakpoints = { ["lg"] = "64rem", ["sm"] = "640px" },
            Containers = { ["lg"] = "32rem", ["sm"] = "384px" },
        };
    }

    private static async Task<string> SortAsync(ProjectCompletionValues values, string input)
    {
        var manager = new ProjectConfigurationManager();
        manager.Seed(FilePath, values);
        manager.SeedDefault(values);
        var utilities = new ClassSortUtilities(
            classOrders: new()
            {
                [values.Version] = new()
                {
                    ["block"] = 0,
                    ["p-{s}"] = 10,
                    ["-m-{s}"] = 20,
                    ["text-{c}"] = 30,
                    ["font-bold"] = 40,
                    ["w-{f}"] = 50,
                    ["flex-{n}"] = 60,
                    ["opacity-{%}"] = 70,
                    ["translate-x-{a}"] = 80,
                },
            },
            variantOrders: new()
            {
                [values.Version] = new()
                {
                    ["hover"] = 100,
                    ["focus"] = 200,
                    ["{b}"] = 300,
                    ["@{c}"] = 400,
                },
            }
        );
        var sorter = new Sorter();
        typeof(Sorter)
            .GetField(
                "_projectConfigurationManager",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .SetValue(sorter, manager);
        typeof(Sorter)
            .GetField("_classSortUtilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(sorter, utilities);

        var classes = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", await sorter.OperateAsync(classes, FilePath));
    }
}
