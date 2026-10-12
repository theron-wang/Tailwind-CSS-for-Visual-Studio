using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;
using TailwindCSSIntellisense.ClassOperations;
using TailwindCSSIntellisense.ClassOperations.Operators;
using TailwindCSSIntellisense.ClassOperations.Sorters;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;
using TailwindCSSIntellisense.Tests.Stubs;
using ClassNameCanonicalizer = TailwindCSSIntellisense.ClassOperations.Canonicalizers.Canonicalizer;
using HtmlCanonicalizer = TailwindCSSIntellisense.ClassOperations.Canonicalizers.HtmlCanonicalizer;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

public class ClassOperationPipelineTests
{
    [Fact]
    public async Task Pipeline_CanonicalizesEveryGroupInOneRunWithWarmCache()
    {
        using var canonicalizer = new CanonicalizerStub();
        await canonicalizer.CanonicalizeAsync(["px-2"], "index.html");
        var pipeline = CreatePipeline(canonicalizer);
        var settings = CreateSettings();
        settings.SortClassesType = SortClassesOptions.None;
        const string content = "<div class='px-2'></div><div class='text-[#fff] py-2 px-2'></div>";

        var result = await pipeline.ProcessAsync("index.html", content, settings);

        Assert.Equal("<div class='px-2'></div><div class='text-white p-2'></div>", result);
        Assert.Equal(result, await pipeline.ProcessAsync("index.html", result, settings));
    }

    [Theory]
    [InlineData(typeof(ClassSorter), "px-2 py-2 text-[#fff]")]
    [InlineData(typeof(ClassCanonicalizer), "text-white p-2")]
    public async Task Pipeline_ManualSelectionRunsOnlyTheRequestedOperation(
        Type operationType,
        string expected
    )
    {
        var settings = CreateSettings();
        settings.SortClassesType = SortClassesOptions.OnSave;
        settings.CanonicalizeClassesType = SortClassesOptions.OnBuild;

        var result = await CreatePipeline()
            .ProcessAsync(
                "index.html",
                "<div class='text-[#fff] px-2 py-2'></div>",
                settings,
                operationType: operationType
            );

        Assert.Equal($"<div class='{expected}'></div>", result);
    }

    [Theory]
    [InlineData(SortClassesOptions.None, false)]
    [InlineData(SortClassesOptions.Manual, true)]
    [InlineData(SortClassesOptions.OnSave, true)]
    [InlineData(SortClassesOptions.OnBuild, true)]
    public async Task Pipeline_ManualSelectionRespectsEachOperationsSetting(
        SortClassesOptions mode,
        bool enabled
    )
    {
        var pipeline = CreatePipeline();
        const string content = "<div class='text-[#fff] px-2 py-2'></div>";

        foreach (var operationType in new[] { typeof(ClassSorter), typeof(ClassCanonicalizer) })
        {
            var settings = CreateSettings();
            settings.SortClassesType =
                operationType == typeof(ClassSorter) ? mode : SortClassesOptions.None;
            settings.CanonicalizeClassesType =
                operationType == typeof(ClassCanonicalizer) ? mode : SortClassesOptions.None;
            Assert.Equal(
                enabled,
                pipeline.GetEnabledOperations(settings, operationType: operationType).Any()
            );
            var result = await pipeline.ProcessAsync(
                "index.html",
                content,
                settings,
                operationType: operationType
            );
            Assert.Equal(enabled, result != content);
        }
    }

    [Fact]
    public async Task Pipeline_CanonicalizesBeforeSortingRegardlessOfImportOrder()
    {
        var pipeline = CreatePipeline();
        var settings = CreateSettings();
        const string content = "<div class='text-[#fff] px-2 py-2'></div>";

        var result = await pipeline.ProcessAsync("index.HTML", content, settings);

        Assert.Equal("<div class='p-2 text-white'></div>", result);
    }

    [Fact]
    public async Task Pipeline_ComposesOperationsAndInheritedImportsThroughMef()
    {
        using var catalog = new TypeCatalog(
            typeof(ClassOperationAggregator),
            typeof(ClassSorter),
            typeof(ClassCanonicalizer),
            typeof(Sorter),
            typeof(ClassNameCanonicalizer),
            typeof(HtmlSorter),
            typeof(HtmlCanonicalizer)
        );
        using var container = new CompositionContainer(catalog);
        var manager = new ProjectConfigurationManager();
        manager.SeedDefault(new ProjectCompletionValues { Version = TailwindVersion.V4_2 });
        var batch = new CompositionBatch();
        batch.AddExportedValue(manager);
        batch.AddExportedValue(
            new ClassSortUtilities(
                new() { [TailwindVersion.V4_2] = [] },
                new() { [TailwindVersion.V4_2] = [] }
            )
        );
        batch.AddExportedValue<Linting.Validators.Canonicalization.Canonicalizer>(
            new CanonicalizerStub()
        );
        container.Compose(batch);

        var pipeline = container.GetExportedValue<ClassOperationAggregator>()!;

        Assert.Collection(
            pipeline.GetEnabledOperations(CreateSettings()),
            op => Assert.IsType<ClassCanonicalizer>(op),
            op => Assert.IsType<ClassSorter>(op)
        );
        Assert.Equal(
            "<div class='text-white'></div>",
            await pipeline.ProcessAsync(
                "index.html",
                "<div class='text-[#fff]'></div>",
                CreateSettings()
            )
        );
    }

    [Theory]
    [InlineData(SortClassesOptions.None, SortClassesOptions.Manual, "text-white p-2")]
    [InlineData(SortClassesOptions.Manual, SortClassesOptions.None, "px-2 py-2 text-[#fff]")]
    [InlineData(SortClassesOptions.None, SortClassesOptions.None, "text-[#fff] px-2 py-2")]
    public async Task Pipeline_AppliesOnlyEnabledOperations(
        SortClassesOptions sorting,
        SortClassesOptions canonicalization,
        string expected
    )
    {
        var settings = CreateSettings();
        settings.SortClassesType = sorting;
        settings.CanonicalizeClassesType = canonicalization;

        var result = await CreatePipeline()
            .ProcessAsync("index.html", "<div class='text-[#fff] px-2 py-2'></div>", settings);

        Assert.Equal($"<div class='{expected}'></div>", result);
    }

    [Theory]
    [InlineData(SortClassesOptions.OnSave, "px-2 py-2 text-[#fff]")]
    [InlineData(SortClassesOptions.OnBuild, "text-white p-2")]
    public async Task Pipeline_FiltersEachOperationByTrigger(
        SortClassesOptions trigger,
        string expected
    )
    {
        var settings = CreateSettings();
        settings.SortClassesType = SortClassesOptions.OnSave;
        settings.CanonicalizeClassesType = SortClassesOptions.OnBuild;

        var result = await CreatePipeline()
            .ProcessAsync(
                "index.html",
                "<div class='text-[#fff] px-2 py-2'></div>",
                settings,
                trigger
            );

        Assert.Equal($"<div class='{expected}'></div>", result);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Pipeline_RequiresExtensionAndConfiguration(bool enabled, bool configured)
    {
        var settings = CreateSettings();
        settings.EnableTailwindCss = enabled;
        if (!configured)
            settings.ConfigurationFiles.Clear();
        const string content = "<div class='text-[#fff] px-2 py-2'></div>";

        Assert.Equal(content, await CreatePipeline().ProcessAsync("index.html", content, settings));
    }

    [Fact]
    public async Task Pipeline_SkipsUnsupportedFiles()
    {
        const string content = "<div class='text-[#fff] px-2 py-2'></div>";
        Assert.Equal(
            content,
            await CreatePipeline().ProcessAsync("index.txt", content, CreateSettings())
        );
    }

    [Fact]
    public async Task Pipeline_AllowsAdditionalOperationsWithIndependentFileTypesAndPriorities()
    {
        var calls = new List<int>();
        var pipeline = new ClassOperationAggregator([
            new RecordingOperation(200, ".html", calls),
            new RecordingOperation(0, ".txt", calls),
            new RecordingOperation(50, ".html", calls),
            new RecordingOperation(100, ".html", calls),
        ]);

        Assert.Equal(
            "content50100200",
            await pipeline.ProcessAsync("index.html", "content", CreateSettings())
        );
        Assert.Equal([50, 100, 200], calls);
    }

    private static TailwindSettings CreateSettings() =>
        new()
        {
            EnableTailwindCss = true,
            ConfigurationFiles = ["tailwind.css"],
            SortClassesType = SortClassesOptions.Manual,
            CanonicalizeClassesType = SortClassesOptions.Manual,
        };

    private static ClassOperationAggregator CreatePipeline(
        CanonicalizerStub? canonicalizerStub = null
    )
    {
        var canonicalizer = new ClassNameCanonicalizer();
        typeof(ClassNameCanonicalizer)
            .GetField("_canonicalizer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(canonicalizer, canonicalizerStub ?? new CanonicalizerStub());
        var project = new ProjectCompletionValues { Version = TailwindVersion.V4_2 };
        project.SpacingMapper["2"] = "0.5rem";
        project.ColorMapper["white"] = "#fff";
        var manager = new ProjectConfigurationManager();
        manager.SeedDefault(project);
        var sorter = new Sorter();
        typeof(Sorter)
            .GetField(
                "_projectConfigurationManager",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .SetValue(sorter, manager);
        typeof(Sorter)
            .GetField("_classSortUtilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(
                sorter,
                new ClassSortUtilities(
                    new()
                    {
                        [TailwindVersion.V4_2] = new()
                        {
                            ["p-{s}"] = 0,
                            ["px-{s}"] = 1,
                            ["py-{s}"] = 2,
                            ["text-{c}"] = 3,
                        },
                    },
                    new() { [TailwindVersion.V4_2] = [] }
                )
            );
        return new ClassOperationAggregator([
            new ClassSorter { Operators = [CreateOperator<HtmlSorter, Sorter>(sorter)] },
            new ClassCanonicalizer
            {
                Operators =
                [
                    CreateOperator<HtmlCanonicalizer, ClassNameCanonicalizer>(canonicalizer),
                ],
            },
        ]);
    }

    private static TOperator CreateOperator<TOperator, TOperation>(TOperation operation)
        where TOperator : ClassOperator<TOperation>, new()
        where TOperation : IClassOperation, new()
    {
        var op = new TOperator();
        typeof(ClassOperator<TOperation>)
            .GetField("_operation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(op, operation);
        return op;
    }

    private sealed class RecordingOperation(int priority, string extension, List<int> calls)
        : ClassOperation
    {
        public override int Priority => priority;
        public override IEnumerable<string> AllHandled => [extension];

        protected override SortClassesOptions GetMode(TailwindSettings settings) =>
            settings.SortClassesType;

        public override Task<string> ProcessAsync(string path, string content)
        {
            calls.Add(priority);
            return Task.FromResult(content + priority);
        }
    }
}
