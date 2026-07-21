# AGENTS.md

## Repository Overview

This repository contains the **Tailwind CSS IntelliSense** Visual Studio extension.

- Main extension code: `/src`
- Automated tests: `/tests/TailwindCSSIntellisense.Tests`
- CI workflow: `/.github/workflows/tests.yml`

## Local Development Expectations

Prerequisites:

- .NET SDK 10
- Node.js + npm (used by extension features)

Recommended validation commands from the repository root:

```bash
dotnet tool restore
dotnet csharpier check .
dotnet test tests/TailwindCSSIntellisense.Tests/TailwindCSSIntellisense.Tests.csproj --configuration Release --verbosity minimal
```

Notes:

- `dotnet csharpier check .` can report pre-existing line-ending formatting differences.
- Full extension build may require Visual Studio SDK targets that are typically available on Windows environments.

## Change Guidelines

- Keep changes focused and minimal.
- Do not modify unrelated files.
- Prefer updating or adding tests under:
  - `UnitTests` for isolated logic
  - `IntegrationTests` for cross-module scenarios
- Follow existing code style and formatting conventions.
- Never commit secrets or credentials.
