using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

[Export(typeof(DiagnosticsAggregator))]
[method: ImportingConstructor]
internal class DiagnosticsAggregator([ImportMany] IEnumerable<DiagnosticsChecker> generators)
{
    private readonly IEnumerable<DiagnosticsChecker> _generators = generators;

    public async IAsyncEnumerable<Error> GetErrorsAsync(
        SnapshotSpan span,
        bool isCss,
        ProjectCompletionValues projectCompletionValues,
        Func<string, IEnumerable<Match>> findClasses,
        Func<string, IEnumerable<Match>> splitClasses,
        Func<SnapshotSpan, bool> shouldNotAddErrors,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var applicableGenerators = isCss ? _generators : _generators.Where(g => !g.IsCssOnly);

        foreach (var generator in applicableGenerators)
        {
            IAsyncEnumerator<Error>? enumerator = null;
            try
            {
                enumerator = generator
                    .GetErrorsAsync(
                        span,
                        projectCompletionValues,
                        findClasses,
                        splitClasses,
                        shouldNotAddErrors
                    )
                    .GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ex.LogAsync();
                continue;
            }

            try
            {
                while (true)
                {
                    Error current;
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                        {
                            break;
                        }
                        current = enumerator.Current;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        await ex.LogAsync();
                        break;
                    }

                    yield return current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }
    }
}
