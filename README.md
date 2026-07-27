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
- Uses Siemens Openness via out-of-process host execution (`TiaProjectExporter.OpennessHost`, .NET Framework 4.8) to avoid .NET 8 in-process loader conflicts.
- Uses Siemens `Siemens.Collaboration.Net.TiaPortal.Openness.Extensions` (`20.0.1744193700`) in the host and initializes the Siemens resolver API before first Openness access, with manual path loading as fallback.
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
- `src/TiaProjectExporter.OpennessHost` — isolated Siemens Openness runtime host (net48)
- `src/TiaProjectExporter.Export` — export stages + analysis/report generation
- `src/TiaProjectExporter.UI` — WPF MVVM desktop application
- `tests/TiaProjectExporter.Tests` — xUnit tests

The exporter uses a pipeline of independent stages (`IExportStage`) so failures in one area are captured as issues without crashing the whole export.

## Prerequisites (Windows)

- Windows 10/11
- Visual Studio 2022 (or newer) with:
  - `.NET desktop development` workload (for WPF)
  - .NET 8 SDK
  - .NET Framework 4.8 Developer Pack (required to build `TiaProjectExporter.OpennessHost`)
- TIA Portal installation(s), ideally V18/V19/V20 with Openness available
- Access rights to open target TIA project files
- Access to `nuget.org` (or mirrored internal feed) for Siemens package restore in `TiaProjectExporter.OpennessHost`

## SDK pinning

This repository pins SDK in `global.json`:

- `8.0.423`

If build complains about SDK mismatch, install that SDK first.

## Versioning

- Current application version: `0.0.31`

- `TiaProjectExporter.OpennessHost` treats `NU1603` as warning-only (not error) because Siemens transitive dependency lower-bound versions are not currently available on `nuget.org`; closest higher compatible versions are restored and warning visibility is retained.
- Traversal hardening: host reflection walk now applies candidate-property filtering, per-node/per-enumerable limits, and slow-property diagnostics to reduce hangs on heavy runtime nodes during deep project export.
- Host-response parsing hardening: out-of-process adapter now accepts `metadata` in both JSON object form and DataContract-style `[{"Key","Value"}]` array form to prevent `JsonException` inventory aborts.
- Host packaging hardening: UI build/publish now copies the full net48 host runtime folder (including NuGet-resolved Siemens collaboration dependencies) so `TiaProjectExporter.OpennessHost.exe` can load all required assemblies at runtime.
- Inventory object export stage now emits per-object artifacts into domain folders (`Export/<Domain>/Objects/...`) in JSON/XML/Markdown (based on selected formats).
- Publish/build orchestration now builds the net48 Openness host without inheriting UI runtime RID settings, preventing `NETSDK1047` (`net48/win-x64`) during UI publish.
- PLC discovery hardening: out-of-process host now performs a dedicated PLC-focused traversal pass over software/block/tag/datatype candidate properties to improve discovery of `OB`/`FB`/`FC`/`DB`/`InstanceDB`/`Tag`/`UDT` objects.
- Metadata depth hardening: host now extracts bounded scalar runtime properties per discovered object (`Prop.*`) to capture more configuration/settings data while guarding performance with limits.
- PLC entry-point hardening: host now probes Siemens `GetService(...)` software-container services while traversing device nodes to discover PLC software trees that are not directly exposed via plain public properties.
- Deep content export: host now attempts `Export(FileInfo)` extraction for software/runtime nodes and captures discovered source-like text fields; object-export stage writes these into sidecar files (`*.content.export.xml`, `*.content.source.*`).
- Content-first object serialization: per-object JSON/XML/Markdown now suppresses verbose `Prop.*` and raw `Content.*` blobs to reduce metadata noise; deep content remains available in sidecar files.
- Bundle-first export: inventory objects are now grouped into domain/type bundle files (`Export/<Domain>/Bundles/<ObjectType>.*`) to drastically reduce file count while keeping deep content (source/XML) in each bundle.
- Duplicate-path hardening: analytics stages now deduplicate repeated inventory IDs/paths before dictionary materialization, preventing `An item with the same key has already been added` failures on large multi-entry traversals.
- Source extraction hardening: host now tries source-oriented runtime methods (`GenerateSource/GetSource/GetText/...`) and XML-content parsing fallback (`Source/StatementList/Implementation/...`) so bundle exports contain readable code whenever Openness exposes it.
- Version is centrally defined in `Directory.Build.props` via `Version`, `AssemblyVersion`, and `FileVersion`.
- The WPF UI shows the current version in the window title/header.

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

### Windows self-contained publish (win-x64)

```powershell
dotnet restore src/TiaProjectExporter.UI/TiaProjectExporter.UI.csproj -r win-x64
dotnet publish src/TiaProjectExporter.UI/TiaProjectExporter.UI.csproj -c Release -r win-x64 --self-contained true
```

If you hit `NETSDK1047` for `net8.0-windows/win-x64`, ensure publish restore runs with `-r win-x64` (or remove `--no-restore` and let publish restore with runtime).

Note: publish output now includes `TiaProjectExporter.OpennessHost.exe` (net48), which is required for stable Siemens Openness execution.

If Siemens build targets log "Unable to locate Siemens.Engineering assemblies" during restore/build, pass the local PublicAPI folder explicitly, for example:

```powershell
dotnet publish src/TiaProjectExporter.UI/TiaProjectExporter.UI.csproj -c Release -r win-x64 --self-contained true -p:TiaPortalLocation="C:\Program Files\Siemens\Automation\Portal V20\PublicAPI"
```

Windows validation workflow artifacts:

- `docs/WINDOWS_VALIDATION.md`
- `scripts/validate-windows.ps1`

## How to use the UI

1. Start `TiaProjectExporter.UI`.
2. Click **Detect Versions** (optional but recommended).
   - Click **Health Check** to verify host deployment and Siemens runtime loadability before export.
3. Enter/select:
   - **Project Path**: source TIA project (`.ap18`, `.ap19`, `.ap20`)
     - use **Browse** to pick the project file
     - use **Validate Project** to verify path exists and extension is supported
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
- Export now requires a valid project path; if invalid, export does not start and validation feedback is shown in UI.

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

Per-object inventory artifacts are written under domain folders, for example:

- `Hardware/Objects/*.json|*.xml|*.md`
- `Blocks/Objects/*.json|*.xml|*.md`
- `Tags/Objects/*.json|*.xml|*.md`
- `UDTs/Objects/*.json|*.xml|*.md`
- `HMI/Objects/*.json|*.xml|*.md`

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

- **Export finishes but contains only standard files**
  - Click **Validate Project** and ensure project path points to an existing `.ap18`, `.ap19`, or `.ap20` project.
  - Check `Export/Reports/TIA_PROJECT_INVENTORY.md` and `Export/EXPORT_REPORT.md` for traversal issues.
  - If inventory status is `Unavailable` or `Partial`, the run is now reported as "completed with issues" instead of plain success.
  - Openness traversal errors now include unwrapped inner exception details to make root-cause diagnosis (version mismatch, access mode, lock/session issues) easier.

- **Export aborts and no log appears in output**
  - The UI now writes `Export/Reports/EXPORT_FAILURE.log` on command/export failures, including exception stack and UI log snapshot.
  - Application-level crashes are additionally written under `%LocalAppData%/TiaProjectExporter/CrashLogs`.
  - If the selected output path is not writable, fallback diagnostics are written to `%LocalAppData%/TiaProjectExporter/FailureDiagnostics`.
  - Host stderr (including raw heartbeat lines) is always written to `%LocalAppData%/TiaProjectExporter/HostLogs/host-stderr-*.log`.

- **`Siemens.Engineering.Contract` / `MissingMethodException` crashes**
  - The exporter now executes Openness in a separate host process (`TiaProjectExporter.OpennessHost.exe`, .NET Framework 4.8) to avoid .NET 8 in-process loader incompatibilities.
  - Ensure the host executable is deployed beside the UI executable (or set environment variable `TIA_EXPORTER_OPENNESS_HOST_PATH`).
  - Use the UI **Health Check** button (traffic-light indicator) to validate host + runtime state before running export.
  - Export now runs an automatic preflight health check and output-writeability check before pipeline start, and aborts early with explicit log messages if preconditions fail.

- **Export seems stuck while CPU is high**
  - The host now emits live heartbeats; UI shows a dedicated host-activity traffic light.
  - Timeout guidance used by UI indicator:
    - `<=15s` since last heartbeat: green (active)
    - `16-60s`: yellow (delayed)
    - `>60s`: red (stale, consider cancel/retry if persistent)
  - Custom corporate installations may still require registry policy exceptions.

## Development workflow note

This repository is developed milestone-by-milestone.

- `PROJECT.md` tracks architecture, current milestone, TODOs, completed tasks, known issues, future improvements, and build instructions.
- This `README.md` must be updated whenever implemented functionality changes user-visible behavior, setup, output artifacts, or usage steps.
