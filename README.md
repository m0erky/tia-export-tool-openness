# TIA Project Exporter

Production-oriented Windows desktop application (WPF, .NET 8) that exports Siemens TIA Portal projects into a structured, AI-friendly repository.

The export is designed to be useful for:

- Git version control and code review
- VS Code browsing/search
- LLM-assisted understanding (Codex, GPT, Claude, Gemini, local models)
- Human-readable engineering documentation

## Why this tool exists

TIA projects are rich but difficult to diff, inspect, and analyze outside TIA Portal. This tool creates a normalized export with:

- machine-friendly formats (`JSON`, `XML`)
- readable summaries (`Markdown`)
- relationship and readiness analysis artifacts
- resilient stage-based execution (continues even when parts fail)

## Current capabilities

- Detects installed TIA versions (targeting V18/V19/V20) via Windows registry.
- Uses Siemens Openness via reflection-safe adapter design.
- Exports traversal inventory and metadata into structured artifacts.
- Generates analysis artifacts, including:
  - call graph
  - dependency graph
  - executive summary
  - relationship insights
  - coverage matrix
  - readiness scoring
  - next-best-actions backlog
  - runtime type catalog
  - typed-extractor implementation backlog
  - mapping implementation tracker with cross-run trend
  - domain extractor coverage matrix and gap list
  - validation workflow assets for Windows (`docs/WINDOWS_VALIDATION.md`, `scripts/validate-windows.ps1`)

## Architecture

Solution projects:

- `src/TiaProjectExporter.Core` — core domain models/contracts
- `src/TiaProjectExporter.Application` — orchestration/use-cases
- `src/TiaProjectExporter.Infrastructure` — file/serialization/infrastructure helpers
- `src/TiaProjectExporter.Tia` — TIA discovery + Openness adapter + domain extractors
- `src/TiaProjectExporter.Export` — export stages + analysis/report generation
- `src/TiaProjectExporter.UI` — WPF MVVM desktop application
- `tests/TiaProjectExporter.Tests` — xUnit tests

The exporter uses a pipeline of independent stages (`IExportStage`) so failures in one area are captured as issues without crashing the whole export.

## Prerequisites (Windows)

- Windows 10/11
- Visual Studio 2022 (or newer) with:
  - `.NET desktop development` workload (for WPF)
  - .NET 8 SDK
- TIA Portal installation(s), ideally V18/V19/V20 with Openness available
- Access rights to open target TIA project files

## SDK pinning

This repository pins SDK in `global.json`:

- `8.0.423`

If build complains about SDK mismatch, install that SDK first.

## Build and test

### Visual Studio 2022

1. Open `TiaProjectExporter.sln`
2. Select `Debug | Any CPU`
3. Build solution
4. Set `TiaProjectExporter.UI` as startup project
5. Run

### CLI

```bash
dotnet restore
dotnet build TiaProjectExporter.sln
dotnet test tests/TiaProjectExporter.Tests/TiaProjectExporter.Tests.csproj
```

Windows validation workflow artifacts:

- `docs/WINDOWS_VALIDATION.md`
- `scripts/validate-windows.ps1`

## How to use the UI

1. Start `TiaProjectExporter.UI`.
2. Click **Detect Versions** (optional but recommended).
3. Enter/select:
   - **Project Path**: source TIA project
   - **Output Directory**: export destination
   - **TIA Installation Path Override (optional)**: manual TIA root path (for example `C:\Program Files\Siemens\Automation\Portal V20`) when auto detection fails
     - use **Browse** to select the folder
     - use **Validate Path** to check for **TIA V20 + Openness** (`Siemens.Engineering.dll`)
4. Choose options:
   - formats (`JSON`, `XML`, `Markdown`)
   - `Enable Compression`
   - `Skip Diagnostics`
5. Click **Export**.
6. Monitor progress, current object, counters, logs.
7. Review generated artifacts under `<OutputDirectory>/Export`.

Notes:

- Output directory history and settings are persisted in user profile (`LocalApplicationData`).
- Export can be canceled from the UI; cancellation is cooperative.

## Export output structure

Top-level output root is `<OutputDirectory>/Export`.

Representative directories:

- `Project`
- `Hardware`
- `Network`
- `PLC`
- `Blocks`
- `Tags`
- `UDTs`
- `Technology`
- `Libraries`
- `HMI`
- `Diagnostics`
- `Metadata`
- `Reports`

Representative generated files:

- `README.md`
- `PROJECT_OVERVIEW.md`
- `EXECUTIVE_SUMMARY.md`
- `EXPORT_REPORT.md`
- `PROJECT_STATISTICS.json`
- `BLOCK_CALL_GRAPH.md`
- `DEPENDENCIES.json`
- `FILE_INDEX.json`
- `SEARCH_INDEX.json`
- `PROJECT_TREE.txt`
- `Reports/EXPORT_COVERAGE_MATRIX.json`
- `Reports/EXPORT_COVERAGE_MATRIX.md`
- `Reports/RELATIONSHIP_INSIGHTS.json`
- `Reports/RELATIONSHIP_INSIGHTS.md`
- `Reports/EXPORT_READINESS_SCORE.json`
- `Reports/EXPORT_READINESS_SCORE.md`
- `Reports/NEXT_BEST_ACTIONS.json`
- `Reports/NEXT_BEST_ACTIONS.md`
- `Reports/RUNTIME_TYPE_CATALOG.json`
- `Reports/RUNTIME_TYPE_CATALOG.md`
- `Reports/TYPED_EXTRACTOR_BACKLOG.json`
- `Reports/TYPED_EXTRACTOR_BACKLOG.md`
- `Reports/MAPPING_IMPLEMENTATION_TRACKER.json`
- `Reports/MAPPING_IMPLEMENTATION_TRACKER.md`
- `Reports/MAPPING_IMPLEMENTATION_TRACKER_HISTORY.json`
- `Reports/DOMAIN_EXTRACTOR_COVERAGE.json`
- `Reports/DOMAIN_EXTRACTOR_COVERAGE.md`
- `Reports/EXECUTIVE_SUMMARY.json`

## Configuration

UI defaults are loaded from:

- `src/TiaProjectExporter.UI/appsettings.json`

Current keys:

- `Exporter:DefaultOutputDirectory`
- `Exporter:GenerateMarkdownSummaries`
- `Exporter:EnableCompression`
- `Exporter:SkipDiagnostics`

## Reliability model

- Stage failures are captured as recoverable issues.
- Export attempts to continue even if individual objects fail.
- Reports aggregate successes/failures/issues for post-run review.

## Known limitations

- Full Siemens runtime/object coverage is still evolving and partly heuristic.
- Some relationship resolution still uses names/paths and should be upgraded to Siemens-native IDs.
- Final validation requires real V18/V19/V20 projects on Windows.
- Linux can run most tests, but Siemens Openness runtime itself is Windows-only.

## Troubleshooting

- **TIA installation not detected on Windows**
  - Start the UI as a user that can read machine registry keys.
  - Verify TIA Portal and Openness are installed for that machine/user.
  - Re-run **Detect Versions** after installing or repairing TIA components.
  - The discovery logic now probes multiple Siemens registry layouts and uninstall entries for V18/V19/V20, and excludes non-product matches (for example `TIA Portal Help Viewer`).
  - If detection still fails in your environment, set **TIA Installation Path Override (optional)** to the TIA installation root and click **Validate Path**.
  - A valid V20+Openness override requires:
    - path looks like TIA V20 (for example contains `V20` or has `PublicAPI/V20`)
    - `Siemens.Engineering.dll` is found in the installation root/public API candidates
  - Custom corporate installations may still require registry policy exceptions.

## Development workflow note

This repository is developed milestone-by-milestone.

- `PROJECT.md` tracks architecture, current milestone, TODOs, completed tasks, known issues, future improvements, and build instructions.
- This `README.md` must be updated whenever implemented functionality changes user-visible behavior, setup, output artifacts, or usage steps.
