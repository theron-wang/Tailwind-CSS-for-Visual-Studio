using System.ComponentModel.Composition;
using TailwindCSSIntellisense.ClassOperations.Operators;

namespace TailwindCSSIntellisense.ClassOperations.Sorters;

[Export(typeof(ClassOperator<Sorter>))]
internal sealed class CssSorter : CssClassOperator<Sorter> { }

[Export(typeof(ClassOperator<Sorter>))]
internal sealed class HtmlSorter : HtmlClassOperator<Sorter> { }

[Export(typeof(ClassOperator<Sorter>))]
internal sealed class JsSorter : JsClassOperator<Sorter> { }

[Export(typeof(ClassOperator<Sorter>))]
internal sealed class RazorSorter : RazorClassOperator<Sorter> { }
