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

Milestone 1: Solution foundation

Scope:

- Create the modular .NET 8 solution structure.
- Establish core export contracts and resilient orchestration primitives.
- Add WPF shell with MVVM, DI, config, and logging.
- Add initial installed-version detection abstraction for TIA V18/V19/V20.
- Add first unit tests and verify the solution builds.

## TODO List

- Create the solution and project structure for all architectural layers.
- Implement base domain models for export sessions, discovered TIA versions, export items, and reports.
- Implement application orchestration for a non-crashing export pipeline.
- Implement file-based export writer abstractions for JSON/XML/Markdown.
- Implement initial Windows registry-based TIA installation detection service.
- Implement WPF shell with configuration-backed settings and progress/logging view models.
- Add unit tests for pipeline resilience and report aggregation.
- Add placeholder documentation for build/run constraints on non-Windows environments.
- Define next milestone for real TIA project discovery and repository layout generation.

## Completed Tasks

- Documented the target architecture and first milestone.

## Known Issues

- No code exists yet.
- TIA Portal Openness assemblies are Windows-only and cannot be executed in the current Linux workspace.
- WPF build/runtime verification may be limited in the current environment if Windows desktop targeting packs are unavailable.

## Future Improvements

- Streaming exporters for very large projects to reduce peak memory usage.
- Parallel export scheduling with bounded concurrency for high object counts.
- Optional SQLite-backed search index generation.
- Plugin-based exporters for object-type-specific enrichments.
- Differential export mode for comparing current and previous project snapshots.
- Native call graph and dependency graph generation from block references.

## Build Instructions

Planned local build commands:

```bash
dotnet restore
dotnet build TiaProjectExporter.sln
dotnet test TiaProjectExporter.sln
```

Windows-specific notes:

- The UI project targets WPF and should be built on Windows with the .NET 8 SDK and Windows Desktop workload available.
- TIA Openness integration requires Siemens TIA Portal installations and compatible Openness assemblies for V18/V19/V20.
