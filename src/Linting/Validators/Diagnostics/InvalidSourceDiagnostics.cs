using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Linting.Validators.Diagnostics;

/// <summary>
/// https://github.com/tailwindlabs/tailwindcss-intellisense/blob/main/packages/tailwindcss-language-service/src/diagnostics/getInvalidSourceDiagnostics.ts
/// </summary>
[Export(typeof(DiagnosticsChecker))]
internal class InvalidSourceDiagnostics() : CssDiagnosticsChecker(ErrorType.InvalidSource)
{
    private static readonly Regex _importSourceRegex = new(
        @"(?<=\s|^)@(?<directive>(?:import|reference))\s*(?<path>'[^']*'|""[^""]*"")\s*(?:layer\([^)]+\)\s*)?source\((?<source>'[^']*'?|""[^""]*""?|[a-z]*|\)|;)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );

    private static readonly Regex _utilSourceRegex = new(
        @"(?<=\s|^)@(?<directive>tailwind)\s+(?<layer>\S+)\s+source\((?<source>'[^']*'?|""[^""]*""?|[a-z]*|\)|;)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );

    private static readonly Regex _atSourceRegex = new(
        @"(?<=\s|^)@(?<directive>source)\s*(?<not>not)?\s*(?<source>'[^']*'?|""[^""]*""?|[a-z]*(?:\([^)]+\))?|\)|;)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );

    private static readonly Regex _hasDriveLetter = new(
        @"^[A-Z]:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1)
    );

    protected override IEnumerable<Error> GetErrorsImpl(
        SnapshotSpan span,
        ProjectCompletionValues projectCompletionValues,
        Func<string, IEnumerable<Match>> findClasses,
        Func<string, IEnumerable<Match>> splitClasses,
        Func<SnapshotSpan, bool> shouldNotAddErrors
    )
    {
        var (start, text) = GetFullScopeWithoutCssComments(span);

        var matches = _importSourceRegex
            .Matches(text)
            .Cast<Match>()
            .Concat(_utilSourceRegex.Matches(text).Cast<Match>())
            .Concat(_atSourceRegex.Matches(text).Cast<Match>());

        foreach (var match in matches)
        {
            var directive = match.Groups["directive"].Value;
            var sourceGroup = match.Groups["source"];
            var rawSource = sourceGroup.Value.Trim();
            var source = rawSource;
            var isQuoted = false;

            if (source.StartsWith("'") || source.StartsWith("\""))
            {
                source = source.Substring(1);
                isQuoted = true;
            }

            if (source.EndsWith("'") || source.EndsWith("\""))
            {
                source = source.Substring(0, source.Length - 1);
                isQuoted = true;
            }

            source = source.Trim();

            var errorSpan = span.Snapshot.CreateTrackingSpan(
                start + sourceGroup.Index,
                sourceGroup.Length,
                SpanTrackingMode.EdgeExclusive
            );

            if (source == "" || source == ")" || source == ";")
            {
                yield return new Error
                {
                    Span = errorSpan,
                    ErrorMessage = "The source directive requires a path to a directory.",
                    ErrorType = ErrorType.InvalidSource,
                };
            }
            else if (directive != "source" && source != "none" && !isQuoted)
            {
                yield return new Error
                {
                    Span = errorSpan,
                    ErrorMessage = $"'source({source})' is invalid. Did you mean 'source(none)'?",
                    ErrorType = ErrorType.InvalidSource,
                };
            }
            else if (source.Contains('\\') || _hasDriveLetter.IsMatch(source))
            {
                source = source.Replace("\\\\", "\\");

                yield return new Error
                {
                    Span = errorSpan,
                    ErrorMessage =
                        $"POSIX-style paths are required with 'source(…)' but '{source}' is a Windows-style path.",
                    ErrorType = ErrorType.InvalidSource,
                };
            }
            else if (directive == "source" && source == "none")
            {
                yield return new Error
                {
                    Span = errorSpan,
                    ErrorMessage =
                        "'@source none;' is not valid. Did you mean to use 'source(none)' on an '@import'?",
                    ErrorType = ErrorType.InvalidSource,
                };
            }
            else if (directive == "source" && source.StartsWith("inline("))
            {
                // valid, no error
            }
            else if (directive == "source" && source != "none" && !isQuoted)
            {
                yield return new Error
                {
                    Span = errorSpan,
                    ErrorMessage = $"'@source {rawSource};' is invalid.",
                    ErrorType = ErrorType.InvalidSource,
                };
            }
        }
    }
}
