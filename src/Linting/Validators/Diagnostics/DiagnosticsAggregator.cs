using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

[Export(typeof(DiagnosticsAggregator))]
[method: ImportingConstructor]
internal class DiagnosticsAggregator([ImportMany] IEnumerable<DiagnosticsChecker> generators)
{
    private readonly IEnumerable<DiagnosticsChecker> _generators = generators;

    public IEnumerable<Error> GetErrors(
        SnapshotSpan span,
        bool isCss,
        ProjectCompletionValues projectCompletionValues,
        Func<string, IEnumerable<Match>> findClasses,
        Func<string, IEnumerable<Match>> splitClasses,
        Func<SnapshotSpan, bool> shouldNotAddErrors
    )
    {
        var applicableGenerators = isCss ? _generators : _generators.Where(g => !g.IsCssOnly);
        return applicableGenerators.SelectMany(g =>
            g.GetErrors(
                span,
                projectCompletionValues,
                findClasses,
                splitClasses,
                shouldNotAddErrors
            )
        );
    }
}
