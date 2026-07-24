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

- Implement a concrete Siemens Openness-backed inventory provider for project metadata, device tree, and software discovery.
- Add cancellation support through the UI workflow.
- Add configuration persistence for recent output folders and export format selections.
- Add unit tests for repository layout generation and TIA installation discovery edge cases.
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

## Known Issues

- TIA Portal Openness assemblies are Windows-only and cannot be executed in the current Linux workspace.
- WPF build/runtime verification still needs confirmation on a Windows machine with the Windows Desktop workload installed.
- Registry-based TIA installation detection currently uses best-effort value probing and needs validation against real customer installations.
- TIA project inventory export currently emits placeholder availability/status artifacts until the Siemens Openness adapter is implemented.
- Siemens.Engineering integration is still pending; current Openness adapter intentionally returns structured placeholder issues.
- `FILE_INDEX.json`, `SEARCH_INDEX.json`, and `BLOCK_CALL_GRAPH.md` are still placeholder-first and need deep object-level export data.

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

Windows-specific notes:

- The UI project targets WPF and should be built on Windows with the .NET 8 SDK and Windows Desktop workload available.
- TIA Openness integration requires Siemens TIA Portal installations and compatible Openness assemblies for V18/V19/V20.
- Linux verification command used successfully in this workspace:

```bash
DOTNET_CLI_HOME=/tmp DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 dotnet test tests/TiaProjectExporter.Tests/TiaProjectExporter.Tests.csproj
```
