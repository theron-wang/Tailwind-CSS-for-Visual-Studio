# Qodana code analysis

Local scans and GitHub Actions use the root `qodana.yaml` configuration with the
recommended inspection profile. Qodana runs natively on Windows because the
extension targets .NET Framework 4.8 and requires Visual Studio SDK build targets.
The scan includes both the extension and the test project.

## Run locally

Install .NET SDK 10 and Visual Studio with the **Visual Studio extension
development** workload and .NET Framework 4.8 targeting pack. Install the
[Qodana CLI](https://github.com/JetBrains/qodana-cli):

```powershell
winget install -e --id JetBrains.QodanaCLI
```

Open a Developer PowerShell for Visual Studio in the repository root. Restore
and build the solution, then set your Qodana Cloud project token in the current
shell and scan:

```powershell
msbuild TailwindCSSIntellisense.slnx /restore /p:Configuration=Release /p:DeployExtension=false
$env:QODANA_TOKEN = "<your-project-token>"
qodana scan --results-dir .qodana/results --cache-dir .qodana/cache
```

Replace the placeholder with your actual token. PowerShell uses `$env:QODANA_TOKEN`
instead of the Bash `QODANA_TOKEN="..." qodana scan` syntax. Once the environment
variable is set, `qodana scan` also works with Qodana's default output directories.
The `.qodana` directory is ignored by Git. Do not put the token in tracked files.

Qodana for .NET requires a Qodana Cloud project token and a compatible license;
see the [JetBrains .NET guide](https://www.jetbrains.com/help/qodana/dotnet.html).

## Run in GitHub Actions

In the repository's **Settings > Secrets and variables > Actions**, add a
repository secret named `QODANA_TOKEN` containing your Qodana Cloud project token.

The `Qodana` workflow runs on pushes to `main`, pull requests from this repository,
and manual dispatches from the Actions tab. Fork pull requests and Dependabot
runs are skipped because they do not have access to the token. The workflow builds
the solution with Visual Studio MSBuild before scanning and uploads the analysis
results as the `qodana-report` workflow artifact. Authenticated scans also send
results to Qodana Cloud.

No issue-count quality gate is configured yet. Add a threshold or baseline to
`qodana.yaml` when you are ready to enforce one.
