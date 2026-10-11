using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.ClassOperations;

[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
[method: ImportingConstructor]
internal sealed class ClassOperationAggregator([ImportMany] IEnumerable<ClassOperation> operations)
{
    private readonly ClassOperation[] _operations = operations.OrderBy(o => o.Priority).ToArray();

    public IEnumerable<ClassOperation> GetEnabledOperations(
        TailwindSettings settings,
        SortClassesOptions? trigger = null,
        Type? operationType = null
    ) =>
        _operations.Where(o =>
            o.IsEnabled(settings, trigger)
            && (operationType is null || operationType.IsInstanceOfType(o))
        );

    public async Task<string> ProcessAsync(
        string path,
        string content,
        TailwindSettings settings,
        SortClassesOptions? trigger = null,
        Type? operationType = null
    )
    {
        foreach (var operation in GetEnabledOperations(settings, trigger, operationType))
        {
            if (operation.Handled(path))
            {
                content = await operation.ProcessAsync(path, content);
            }
        }
        return content;
    }
}
