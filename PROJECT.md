# TIA Project Exporter

## Overall Architecture

The solution follows clean architecture with clear dependency flow toward the domain:

- `TiaProjectExporter.Core`
  - Domain entities, value objects, enums, and core contracts.
  - No dependency on UI, file system, or Siemens APIs.
- `TiaProjectExporter.Application`
  - Use cases, orchestration services, progress reporting, export planning, and report generation.
  - Depends only on `Core`.
- `TiaProjectExporter.Infrastructure`
  - Configuration, file system access, clock/time estimation helpers, and JSON/Markdown/XML export writers.
  - Depends on `Application` and `Core`.
- `TiaProjectExporter.Tia`
  - Siemens TIA Portal Openness integration, installed-version detection, project traversal, and API adapters.
  - Depends on `Application` and `Core`.
- `TiaProjectExporter.Export`
  - Repository layout builder, export packaging, summary generation, and AI-oriented output composition.
  - Depends on `Application`, `Infrastructure`, and `Core`.
- `TiaProjectExporter.UI`
  - WPF MVVM desktop shell, dependency injection bootstrap, configuration binding, progress/log views, and user workflows.
  - Depends on `Application`, `Infrastructure`, `Tia`, `Export`, and `Core`.
- `TiaProjectExporter.Tests`
  - xUnit tests covering domain/application behavior and export pipeline components.

Architectural decisions:

- Exporting is modeled as a resilient pipeline of independent stages so one failing object does not stop the full export.
- TIA-specific logic is isolated behind interfaces to keep the rest of the system testable without Siemens assemblies.
- Output generation is separated from extraction so the application can later support streaming, compression, or alternate repository layouts.
- Progress reporting is event-based and UI-agnostic to support WPF now and possible CLI/service hosts later.

## Current Milestone

Milestone 2: TIA project traversal and object inventory

Scope:

- Define TIA project traversal contracts that isolate Siemens Openness objects from the rest of the system.
- Add the first inventory/export use case for project metadata, device tree, and software object enumeration.
- Expand generated reports from placeholder repository files to real discovered content.
- Prepare the pipeline for per-object exporters and resilient export reporting.

## TODO List

- Implement deep Siemens.Engineering traversal (devices, network, PLC software, blocks, tags, UDTs, HMI, diagnostics) behind the reflection-safe Openness adapter.
- Validate WPF build and runtime behavior on a Windows machine with the .NET 8 SDK installed.
- Validate Windows registry detection against actual TIA V18/V19/V20 installations and adjust key/value probing as needed.

## Completed Tasks

- Documented the target architecture, milestone plan, and build constraints.
- Created the modular .NET 8 solution structure for `Core`, `Application`, `Infrastructure`, `TIA`, `Export`, `UI`, and `Tests`.
- Added strict shared build settings and central package version management.
- Implemented core export models for options, progress updates, results, issues, and reports.
- Implemented a resilient application export coordinator that continues when individual stages fail.
- Added file-system-based artifact writer abstractions with runtime output-root selection.
- Implemented the initial export stage that generates the target repository skeleton and AI-oriented placeholder files.
- Implemented Windows registry-based discovery abstraction for supported TIA Portal V18/V19/V20 installations.
- Added the first WPF MVVM shell with dependency injection, configuration loading, logging, progress display, statistics, and installed-version detection.
- Added initial xUnit tests for pipeline resilience and progress propagation.
- Added TIA project inventory contracts plus an inventory export stage that emits structured JSON/XML/Markdown status artifacts.
- Extended the UI with a source TIA project path field so the next milestone can attach real project traversal.
- Fixed the test project references/imports so Linux-based non-WPF test execution works with the .NET 8 SDK.
- Verified `dotnet test tests/TiaProjectExporter.Tests/TiaProjectExporter.Tests.csproj` passes on Ubuntu 24.04 with .NET SDK 8.0.129.
- Replaced placeholder report artifacts with execution-driven generation for `PROJECT_OVERVIEW.md`, `PROJECT_STATISTICS.json`, and `EXPORT_REPORT.md`.
- Added an `ExportReportStage` plus tests that verify report outputs are generated from real results/issues.
- Added `ITiaProjectOpennessAdapter` and `TiaProjectTraversalResult` abstractions to isolate Siemens traversal concerns from inventory orchestration.
- Replaced the placeholder inventory provider with `OpennessBackedTiaProjectInventoryProvider`, including robust exception-to-issue handling.
- Added a placeholder `UnavailableTiaProjectOpennessAdapter` to keep non-Windows and non-Siemens environments functional while preserving architecture boundaries.
- Added xUnit coverage for inventory provider status mapping (missing path, partial traversal, traversal failure).
- Added export cancellation support in the WPF MVVM workflow with `CancelExportCommand`, cooperative `CancellationTokenSource` handling, and cancellation-specific UI status updates.
- Added unit tests for repository layout generation and non-Windows TIA installation discovery behavior.
- Expanded automated validation to 10 passing xUnit tests in `TiaProjectExporter.Tests`.
- Added user settings persistence (`LocalApplicationData/TiaProjectExporter/user-settings.json`) for output folder history, project path, and export option selections.
- Updated the output folder input to an editable history-backed combobox and persisted settings during export completion and application shutdown.
- Replaced the placeholder Openness adapter with a reflection-safe runtime probe that selects supported installed versions (V18/V19/V20), resolves Siemens.Engineering assembly candidates, and returns structured issues instead of crashing.
- Added non-Windows adapter safety test coverage and expanded automated validation to 11 passing xUnit tests.
- Extended the Openness adapter to attempt real runtime operations through reflection: create `TiaPortal` (without UI), open the selected project, and enumerate top-level devices with resilient fallback issue reporting.
- Added AI-oriented inventory summaries (`AI_PROJECT_SUMMARY`, `AI_HARDWARE_SUMMARY`, `AI_SOFTWARE_SUMMARY`, `AI_PLC_SUMMARY`, `AI_HMI_SUMMARY`, `AI_NETWORK_SUMMARY`) generated from discovered object classifications.
- Implemented execution-context artifact tracking and a dedicated `ExportIndexStage` that generates `FILE_INDEX.json`, `SEARCH_INDEX.json`, and `PROJECT_TREE.txt` from the actual artifacts/directories produced during the run.
- Added index-stage unit tests and expanded automated validation to 12 passing xUnit tests.
- Added a dedicated `BlockCallGraphStage` that generates `BLOCK_CALL_GRAPH.md` from inventory block objects and call metadata (Mermaid + block listing) instead of static placeholder text.
- Added call-graph stage unit tests and expanded automated validation to 13 passing xUnit tests.
- Added a dedicated `DependencyGraphStage` that generates `DEPENDENCIES.json` from discovered inventory object metadata (`Calls`, `DependsOn`, `Uses`, `References`, `Dependencies`) instead of placeholder output.
- Expanded automated validation to 14 passing xUnit tests.
- Added `ObjectUsageAnalysisStage` that generates `TAG_USAGE` and `UNUSED_OBJECTS` artifacts in JSON + Markdown from inventory dependency metadata.
- Expanded automated validation to 15 passing xUnit tests.
- Enhanced `PROJECT_OVERVIEW.md` generation with an analysis aggregator hub that summarizes produced analysis artifacts, key inventory statistics, and dominant object types for faster LLM orientation.
- Added `MultilingualTextStage` that exports centralized multilingual text metadata into `Export/Metadata/MULTILINGUAL_TEXTS.json` and `Export/Metadata/MULTILINGUAL_TEXTS.md` for AI-friendly localization/context analysis.
- Expanded automated validation to 16 passing xUnit tests.
- Added compression packaging support via `CompressionStage` and `ZipExportArchiveService` to generate `Export.zip` when `EnableCompression=true`.
- Expanded automated validation to 18 passing xUnit tests.
- Added root `global.json` pinning .NET SDK `8.0.423` for deterministic local/CI builds.
- Enhanced report/statistics generation with archive metadata sections (`Packaging` in `EXPORT_REPORT.md` and `archive` in `PROJECT_STATISTICS.json`).
- Added archive metadata enrichment with `ExportArchiveInfo` (size and SHA-256) captured during compression and surfaced in report/statistics outputs.

## Known Issues

- TIA Portal Openness assemblies are Windows-only and cannot be executed in the current Linux workspace.
- WPF build/runtime verification still needs confirmation on a Windows machine with the Windows Desktop workload installed.
- WPF cancellation behavior is implemented but still requires Windows runtime validation against real long-running export stages.
- Recent output folder history behavior still needs UX validation on Windows for long path editing and combobox interaction.
- Registry-based TIA installation detection currently uses best-effort value probing and needs validation against real customer installations.
- Full Siemens.Engineering object traversal is still pending; the current adapter validates runtime availability and reports structured readiness issues.
- Runtime reflection signatures may vary across TIA versions; project open/device enumeration behavior requires validation on real V18/V19/V20 Windows installations.
- Block call relationships currently depend on inventory metadata (`Calls`) and still need deep Siemens block-reference extraction from real PLC software objects.
- Dependency relationships currently derive from exported metadata keys and still need deeper Siemens API relationship extraction for complete graph accuracy.
- Tag usage and unused-object detection currently rely on metadata heuristics and still require deeper Siemens semantic references for higher precision.
- Multilingual extraction currently relies on metadata key heuristics and should be extended with direct Siemens language-resource APIs when full traversal is available.
- ZIP packaging has Linux test coverage but still needs end-to-end Windows validation with real large TIA exports.
- Current Linux workspace has SDK `8.0.129`; with `global.json` pinned to `8.0.423`, local `dotnet` commands now require installing SDK `8.0.423` first.
- In this sandbox, running tests with the locally installed `~/.dotnet` SDK can fail due MSBuild named-pipe permission restrictions; verify test pass on a normal host shell/session.
- Function-block call graph, dependency graph, and unused-object detection are still pending and currently represented by placeholder/limited reports.

## Future Improvements

- Streaming exporters for very large projects to reduce peak memory usage.
- Parallel export scheduling with bounded concurrency for high object counts.
- Optional SQLite-backed search index generation.
- Plugin-based exporters for object-type-specific enrichments.
- Differential export mode for comparing current and previous project snapshots.
- Native call graph and dependency graph generation from block references.

## Build Instructions

Expected local build commands:

```bash
dotnet restore
dotnet build TiaProjectExporter.sln
dotnet test TiaProjectExporter.sln
```

SDK pinning:

- `global.json` in the repository root pins the SDK to `8.0.423` (`rollForward: latestPatch`).

Windows-specific notes:

- The UI project targets WPF and should be built on Windows with the .NET 8 SDK and Windows Desktop workload available.
- TIA Openness integration requires Siemens TIA Portal installations and compatible Openness assemblies for V18/V19/V20.
- Linux verification command used successfully in this workspace:

```bash
DOTNET_CLI_HOME=/tmp DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 dotnet test tests/TiaProjectExporter.Tests/TiaProjectExporter.Tests.csproj
```
