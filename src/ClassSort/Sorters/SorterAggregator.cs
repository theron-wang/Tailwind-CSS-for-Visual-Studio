using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TailwindCSSIntellisense.ClassSort.Sorters;

[Export(typeof(SorterAggregator))]
[method: ImportingConstructor]
internal class SorterAggregator([ImportMany] IEnumerable<Sorter> sorters)
{
    private readonly IEnumerable<Sorter> _sorters = sorters;

    public IEnumerable<string> AllHandled => _sorters.SelectMany(s => s.Handled);

    public bool Handled(string file)
    {
        return _sorters.Any(g => g.Handled.Contains(Path.GetExtension(file).ToLowerInvariant()));
    }

    public async Task<string> SortAsync(string filePath, string fileContent)
    {
        var sorter = _sorters.First(g => g.Handled.Contains(Path.GetExtension(filePath).ToLowerInvariant()));
        return await sorter.SortAsync(filePath, fileContent);
    }
}