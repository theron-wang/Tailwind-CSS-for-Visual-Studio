using System.Reflection;
using TailwindCSSIntellisense.ClassOperations.Operators;
using TailwindCSSIntellisense.Tests.Helpers;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class ClassOperatorTests
{
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
