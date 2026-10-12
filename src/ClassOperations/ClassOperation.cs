using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.ClassOperations.Operators;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.ClassOperations;

/// <summary>
/// A configurable file operation. Export this contract to add an operation to the pipeline;
/// lower priorities run first. Manual runs can select a single enabled operation.
/// </summary>
internal abstract class ClassOperation
{
    public abstract int Priority { get; }
    public abstract IEnumerable<string> AllHandled { get; }
    protected abstract SortClassesOptions GetMode(TailwindSettings settings);

    public bool IsEnabled(TailwindSettings settings, SortClassesOptions? trigger = null)
    {
        var mode = GetMode(settings);
        return settings.EnableTailwindCss
            && settings.ConfigurationFiles.Count > 0
            && mode != SortClassesOptions.None
            && (trigger is null || mode == trigger);
    }

    public bool Handled(string path) =>
        AllHandled.Contains(Path.GetExtension(path).ToLowerInvariant());

    public abstract Task<string> ProcessAsync(string path, string content);
}

/// <summary>
/// Shares file-type dispatch between operations using the existing class operators.
/// </summary>
internal abstract class ClassOperation<TOperation> : ClassOperation
    where TOperation : IClassOperation, new()
{
    [ImportMany]
    public IEnumerable<ClassOperator<TOperation>> Operators { get; set; } = [];

    public override IEnumerable<string> AllHandled =>
        Operators.SelectMany(o => o.Handled).Distinct();

    public override Task<string> ProcessAsync(string path, string content)
    {
        var op = Operators.FirstOrDefault(o =>
            o.Handled.Contains(Path.GetExtension(path).ToLowerInvariant())
        );
        return op is null ? Task.FromResult(content) : op.RunOperationAsync(path, content);
    }
}
