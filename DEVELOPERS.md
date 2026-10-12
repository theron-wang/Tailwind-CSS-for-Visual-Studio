# Development

The extension source is in `src`; automated tests are in
`tests/TailwindCSSIntellisense.Tests`.

## Prerequisites

- .NET SDK 10
- Node.js and npm
- Visual Studio with the **Visual Studio extension development** workload and
  .NET Framework 4.8 targeting pack to build the extension

## Local checks

Run from the repository root:

```powershell
dotnet tool restore
dotnet csharpier check .
dotnet test tests/TailwindCSSIntellisense.Tests/TailwindCSSIntellisense.Tests.csproj --configuration Release --verbosity minimal
```

Use `dotnet csharpier format .` to apply formatting. To build the extension,
run this command in Developer PowerShell for Visual Studio:

```powershell
msbuild src/TailwindCSSIntellisense.csproj /restore /p:Configuration=Release /p:DeployExtension=false
```

Keep changes focused and follow the existing style. Add tests under `UnitTests`
for isolated logic or `IntegrationTests` for cross-module behavior. Reuse the
test stubs for Visual Studio services and cover edge cases. Never commit
credentials or secrets.

## Continuous integration and code analysis

The `Tests` workflow runs Release tests and checks formatting on pull requests
and pushes to `main`.

Qodana uses the root `qodana.yaml` to inspect the extension and tests natively on
Windows. The `Qodana` workflow builds the solution before scanning and uploads
the `qodana-report` artifact. Run it manually from the **Actions > Qodana > Run
workflow** menu and select the branch to analyze.
Configure the repository's `QODANA_TOKEN` Actions secret with a Qodana Cloud
project token and compatible license. No issue-count quality gate is configured.

For local analysis, install the [Qodana CLI](https://github.com/JetBrains/qodana-cli),
then run in Developer PowerShell from the repository root:

```powershell
msbuild TailwindCSSIntellisense.slnx /restore /p:Configuration=Release /p:DeployExtension=false
$env:QODANA_TOKEN = "<your-project-token>"
qodana scan --results-dir .qodana/results --cache-dir .qodana/cache
```

Keep the token in your shell environment. The `.qodana` output directory is
ignored by Git. See the [JetBrains .NET guide](https://www.jetbrains.com/help/qodana/dotnet.html)
for licensing and scan setup.
