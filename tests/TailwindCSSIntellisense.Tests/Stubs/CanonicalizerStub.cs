using System.Reflection;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Linting.Validators.Canonicalization;

namespace TailwindCSSIntellisense.Tests.Stubs;

internal sealed class CanonicalizerStub : Canonicalizer
{
    public CanonicalizerStub(TailwindVersion version = TailwindVersion.V4_2)
    {
        var manager = new ProjectConfigurationManager();
        manager.SeedDefault(new ProjectCompletionValues { Version = version });
        typeof(Canonicalizer)
            .GetField(
                "_projectConfigurationManager",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .SetValue(this, manager);
    }

    public List<(string[] ClassNames, string FilePath)> Requests { get; } = [];

    public Func<string[], Task<string[]>> Handler { get; set; } =
        classes =>
        {
            var result = classes.Select(c => c == "text-[#fff]" ? "text-white" : c).ToList();
            if (result.Contains("px-2") && result.Contains("py-2"))
            {
                result.Remove("px-2");
                result.Remove("py-2");
                result.Add("p-2");
            }
            return Task.FromResult(result.ToArray());
        };

    protected override Task<string[]> CanonicalizeViaCliAsync(
        IEnumerable<string> classNames,
        string filePath
    )
    {
        var candidates = classNames.ToArray();
        Requests.Add((candidates, filePath));
        return Handler(candidates);
    }
}
