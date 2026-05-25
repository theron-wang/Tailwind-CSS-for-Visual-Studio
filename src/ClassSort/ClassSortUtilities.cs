using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
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

    private readonly SemaphoreSlim _classOrderLock = new(1, 1);
    private readonly SemaphoreSlim _variantOrderLock = new(1, 1);

    private async Task InitializeClassOrderAsync(TailwindVersion version)
    {
        await _classOrderLock.WaitAsync();
        try
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
        finally
        {
            _classOrderLock.Release();
        }
    }

    private async Task InitializeVariantOrderAsync(TailwindVersion version)
    {
        await _variantOrderLock.WaitAsync();
        try
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
        finally
        {
            _variantOrderLock.Release();
        }
    }

    public async Task<Dictionary<string, int>> GetClassOrderAsync(TailwindVersion version)
    {
        await _classOrderLock.WaitAsync();

        try
        {
            if (_classOrders.TryGetValue(version, out var classOrder))
            {
                return classOrder;
            }
        }
        finally
        {
            _classOrderLock.Release();
        }

        await InitializeClassOrderAsync(version);

        await _classOrderLock.WaitAsync();
        try
        {
            return _classOrders[version];
        }
        finally
        {
            _classOrderLock.Release();
        }
    }

    public async Task<Dictionary<string, int>> GetVariantOrderAsync(TailwindVersion version)
    {
        await _variantOrderLock.WaitAsync();

        try
        {
            if (_variantOrders.TryGetValue(version, out var variantOrder))
            {
                return variantOrder;
            }
        }
        finally
        {
            _variantOrderLock.Release();
        }

        await InitializeVariantOrderAsync(version);

        await _variantOrderLock.WaitAsync();
        try
        {
            return _variantOrders[version];
        }
        finally
        {
            _variantOrderLock.Release();
        }
    }
}