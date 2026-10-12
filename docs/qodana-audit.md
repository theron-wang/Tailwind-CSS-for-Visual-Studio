# Qodana audit

This audit uses the supplied SARIF report for commit
`44f5041b039e036b177aa96ed19d6b6b40e68a19` on the former `canonicalization`
branch. Fixes were applied to current `main` after that branch was merged.
The report contains 760 results, including inspections of the same source in
both the .NET Framework extension and the .NET 10 test project. Its locations
are a historical snapshot, not a new scan of these changes.

## Changes

- Configuration scanning reads the first 15 lines and stops safely at EOF.
- Class operations, Razor matching, font-size descriptions, completion insertion,
  configuration descriptions, project discovery, and conflict detection avoid
  repeated enumeration. Deferred queries no longer capture the subsequently
  modified class stem or loop index.
- File-relative operations resolve a containing directory explicitly, setup
  commands ignore selections without a directory, and process creation checks
  for failure before accessing streams.
- Prefix checks capture a non-null value once. Nullable settings, variants,
  descriptions, and lifecycle state have contracts that reflect their use.
- Code-token searches use ordinal comparisons; CSS opacity formatting uses
  the invariant culture.
- Duplicate checks, unused calculations, redundant casts and boolean comparisons,
  unnecessary imports, XML documentation, and command namespaces were cleaned up.
- The snapshot-span test stub implements typed equality. Disposable test fixtures
  are acquired before assigning properties that could throw.
- Regression coverage checks scan boundaries, single-pass enumerables, relative
  paths, literal string matching, and Tailwind import forms.

## Findings deliberately retained

| Finding | Reason |
| --- | --- |
| `RiderSecurityErrorsInspection` for MessagePack 2.5.192 | Dependency remediation is outside this audit by request. No package versions or dependency declarations were changed. |
| Some `RedundantUsingDirective`, `RedundantEnumerableCastCall`, and nullable-suppression results | The test project's implicit imports and newer framework contracts differ from .NET Framework 4.8. The extension still needs these imports, `MatchCollection.Cast<Match>()`, and some suppressions. Both targets must compile. |
| Unused property accessors and collections populated externally | MEF, Visual Studio options, and JSON serialization use reflection. Removing setters/getters or collections based only on source references would change those contracts. |
| Remaining nullable-contract guards around external or mutable state | Serialized settings and Visual Studio services can supply missing values; defensive guards are preserved where removing them would reduce resilience. |
| `CheckNamespace` in test service stubs | Stubs intentionally have the production namespace so linked production source resolves the expected service type. |
| `UnusedTypeParameter` in test service stubs | Generic signatures mirror Visual Studio APIs used by the linked source. |
| `SuspiciousTypeConversion.Global` for `IVsHierarchy`/`IVsProject` | A Visual Studio COM hierarchy can expose the project interface even though the interfaces do not inherit from one another. The fallback remains necessary. |
| `AccessToDisposedClosure` in the concurrent CLI test | `Task.WhenAll` is awaited before either captured resource leaves its using scope. |
| `AccessToModifiedClosure` in the settings snapshot test | Changing the captured settings is the scenario under test; the earlier enumerable must retain its snapshot. |
| `DisposeOnUsingVariable` in the disposal test | The explicit disposal tests rejection of subsequent requests; the enclosing using also ensures cleanup if an assertion fails. |
| `ReturnValueOfPureMethodIsNotUsed` in regex test cleanup | Enumerating resets the cached custom regex configuration. The output itself is deliberately unused. |
| Historical locations in test stubs and removed code | Some report locations no longer identify the reported construct after the intervening commits. Unrelated current statements are not removed to satisfy stale offsets. |

The restored audit setup adds the Qodana workflow and local scan instructions.
The workflow can produce an updated report when a Qodana token and license are
available. Local compilation and tests do not establish a zero-finding Qodana scan.

## Validation

- `dotnet tool restore`
- `dotnet csharpier check .`
- Visual Studio MSBuild, Release extension build with `DeployExtension=false`
- Release test suite, including the added regressions
- `git diff --check`
