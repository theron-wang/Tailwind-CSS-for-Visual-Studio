using System.ComponentModel.Composition;
using TailwindCSSIntellisense.ClassOperations.Operators;

namespace TailwindCSSIntellisense.ClassOperations.Canonicalizers;

[Export(typeof(ClassOperator<Canonicalizer>))]
internal sealed class CssCanonicalizer : CssClassOperator<Canonicalizer> { }

[Export(typeof(ClassOperator<Canonicalizer>))]
internal sealed class HtmlCanonicalizer : HtmlClassOperator<Canonicalizer> { }

[Export(typeof(ClassOperator<Canonicalizer>))]
internal sealed class JsCanonicalizer : JsClassOperator<Canonicalizer> { }

[Export(typeof(ClassOperator<Canonicalizer>))]
internal sealed class RazorCanonicalizer : RazorClassOperator<Canonicalizer> { }
