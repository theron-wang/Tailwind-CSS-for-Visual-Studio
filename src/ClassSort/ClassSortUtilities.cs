using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Initialization;

namespace TailwindCSSIntellisense.ClassSort;

[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class ClassSortUtilities
{
    private readonly Dictionary<TailwindVersion, Dictionary<string, int>> _classOrders = [];
    private readonly Dictionary<TailwindVersion, Dictionary<string, int>> _variantOrders = [];

    private async Task InitializeClassOrderAsync(TailwindVersion version)
    {
        if (_classOrders.ContainsKey(version))
        {
            return;
        }

        var order = await ResourcesLoader.LoadOrderForVersionAsync(version);

        var classToOrderIndex = new Dictionary<string, int>();
        for (int i = 0; i < order.Count; i++)
        {
            classToOrderIndex[order[i]] = i;
        }

        _classOrders[version] = classToOrderIndex;
    }

    private async Task InitializeVariantOrderAsync(TailwindVersion version)
    {
        if (_variantOrders.ContainsKey(version))
        {
            return;
        }

        var order = await ResourcesLoader.LoadOrderForVersionAsync(version, true);

        var variantToOrderIndex = new Dictionary<string, int>();
        for (int i = 0; i < order.Count; i++)
        {
            // Multiply by 100 so that containers/breakpoints have flexibility
            variantToOrderIndex[order[i]] = i * 100;
        }

        _variantOrders[version] = variantToOrderIndex;
    }

    public async Task<Dictionary<string, int>> GetClassOrderAsync(ProjectCompletionValues project)
    {
        if (_classOrders.TryGetValue(project.Version, out var classOrder))
        {
            return classOrder;
        }
        else
        {
            await InitializeClassOrderAsync(project.Version);
            return _classOrders[project.Version];
        }
    }

    public async Task<Dictionary<string, int>> GetVariantOrderAsync(ProjectCompletionValues project)
    {
        if (_variantOrders.TryGetValue(project.Version, out var variantOrder))
        {
            return variantOrder;
        }
        else
        {
            await InitializeVariantOrderAsync(project.Version);
            return _variantOrders[project.Version];
        }
    }
}
