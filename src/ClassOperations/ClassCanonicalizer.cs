using System.ComponentModel.Composition;
using TailwindCSSIntellisense.ClassOperations.Canonicalizers;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.ClassOperations;

[Export(typeof(ClassOperation))]
internal sealed class ClassCanonicalizer : ClassOperation<Canonicalizer>
{
    public override int Priority => 0;

    protected override SortClassesOptions GetMode(TailwindSettings settings) =>
        settings.CanonicalizeClassesType;
}
