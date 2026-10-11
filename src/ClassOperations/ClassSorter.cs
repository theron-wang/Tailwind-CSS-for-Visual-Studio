using System.ComponentModel.Composition;
using TailwindCSSIntellisense.ClassOperations.Sorters;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.ClassOperations;

[Export(typeof(ClassOperation))]
internal sealed class ClassSorter : ClassOperation<Sorter>
{
    public override int Priority => 100;

    protected override SortClassesOptions GetMode(TailwindSettings settings) =>
        settings.SortClassesType;
}
