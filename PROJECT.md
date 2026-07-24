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
- Add Siemens-API-backed relationship extraction for block calls, tag usage, and dependencies to replace remaining metadata heuristics.
- Expand typed extractor mappings for remaining high-volume runtime nodes currently emitted as `UnmappedRuntimeNode` fallback.
- Validate/extend PLC extractor mappings for instance DBs, block language metadata, and tag-table semantics against real TIA V18/V19/V20 runtime objects.
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
- Extended the reflection-based Siemens Openness adapter to traverse software-level runtime object graphs (beyond devices) and emit categorized nodes (`OB`, `FB`, `FC`, `DB`, `Block`, `Tag`, `UDT`, `Screen`, `Faceplate`, `HMI`) with metadata and reference heuristics (`Calls`, `Dependencies`, multilingual text hints).
- Improved reflective traversal output quality with duplicate suppression (type+path key), extraction-confidence tagging (`ExtractionConfidence`), and explicit extraction strategy metadata (`ExtractionStrategy=ReflectionHeuristic`).
- Introduced a typed Openness domain-extractor architecture (`ITiaDomainExtractor`) with first explicit extractor slices for PLC Blocks, PLC Tags, PLC Data Types, and HMI runtime objects, wired into the reflection adapter for modular maximum-coverage expansion.
- Added `ExportCoverageMatrixStage` to produce `EXPORT_COVERAGE_MATRIX.json` and `EXPORT_COVERAGE_MATRIX.md`, including domain-by-domain discovered counts, high-confidence counts, issue counts, and normalized coverage status (`CompleteCandidate`, `PartialCandidate`, `LowConfidence`, `NotDiscovered`).
- Added explicit `HardwareDomainExtractor` and `NetworkDomainExtractor` implementations and wired them into typed Openness extraction, expanding structured domain coverage beyond PLC/HMI.
- Added focused extractor unit tests covering hardware-module and PROFINET connection extraction behavior.
- Added explicit `LibraryDomainExtractor`, `DiagnosticsDomainExtractor`, and `UsersAuditDomainExtractor` implementations and wired them into typed Openness extraction.
- Added focused extractor unit tests for library type versions, diagnostics severity extraction, and user-role mapping.
- Added explicit `TechnologyDomainExtractor` plus deep HMI extractor split (`HmiScreenFaceplateDomainExtractor`, `HmiRecipeAlarmScriptDomainExtractor`) for finer-grained structured extraction of motion/safety/PID and HMI artifacts.
- Expanded extractor unit tests with technology and deep-HMI cases (safety axis, faceplate, recipe dependency).
- Added explicit `ProjectHierarchyDomainExtractor` for project tree/group/folder structure extraction (including device-group mapping).
- Enhanced extraction metadata with explicit capability flags (`ExtractedByTypedExtractor`, `FallbackReflectionUsed`) and added reflection fallback node capture for unmapped runtime nodes.
- Extended coverage matrix artifacts to include API support and extraction-mode dimensions (`SupportedByApi`, typed counts, fallback counts).
- Enriched hardware extraction metadata with hierarchy depth, parent path, interface count, module category, and slot/position details.
- Enriched network extraction metadata with topology depth, endpoint count, subnet, network type, and protocol details.
- Added reflection fallback hotspot analytics to `PROJECT_OVERVIEW.md`, `EXPORT_REPORT.md`, and `PROJECT_STATISTICS.json`.
- Extended tests for hardware/network metadata enrichment and fallback hotspot reporting.
- Added a domain-aware fallback runtime classifier so unmapped reflection nodes are categorized into `Project`, `Hardware`, `Network`, `PLC`, `HMI`, `Technology`, `Libraries`, `Diagnostics`, and `UsersAudit` buckets with dedicated object types.
- Added unit tests for fallback runtime classification behavior across key Siemens runtime-type patterns.
- Hardened fallback classifier project-domain matching to avoid false-positive classification caused by generic root `Project/...` path prefixes.
- Enriched PLC block extraction metadata with `IsEntryPoint`, `Language`, `BlockNumber`, `TagUsage`, `DataType`, and broader call/dependency reference capture.
- Added explicit `InstanceDB` classification in PLC block extraction for runtime type names containing instance DB markers.
- Enriched PLC tag/tag-table extraction metadata with `DataType`, `Address`, `InitialValue`, `TagUsage`, `TagCount`, and table-level dependency capture.
- Added unit tests for PLC block/tag extraction behavior, including OB relationship metadata, InstanceDB classification, and tag-table metadata extraction.
- Upgraded `BLOCK_CALL_GRAPH.md` generation with entry-point detection, resolved vs unresolved call-edge classification, dashed Mermaid links for unresolved targets, and summary sections for hotspots.
- Upgraded `DEPENDENCIES.json` generation with relationship-aware edges (`Calls`, `DependsOn`, `Uses`, `UsesTag`, `References`), metadata-key provenance, resolved/unresolved edge tracking, and unresolved-target summaries.
- Extended graph-stage tests to verify unresolved target handling, relationship typing, and enriched graph/report sections.
- Added `RelationshipInsightsStage` to generate `Export/Reports/RELATIONSHIP_INSIGHTS.json` and `Export/Reports/RELATIONSHIP_INSIGHTS.md` with AI-oriented relationship summaries, unresolved hotspots, and guidance.
- Registered relationship insights generation in the export pipeline after dependency graph generation.
- Added unit tests for relationship insights artifact generation and relationship/unresolved-edge summaries.
- Added `ExportReadinessStage` to generate `Export/Reports/EXPORT_READINESS_SCORE.json` and `Export/Reports/EXPORT_READINESS_SCORE.md` with domain-level readiness scoring (0-100), unresolved relationship penalties, fallback penalties, and prioritized actions.
- Registered readiness scoring generation in the export pipeline after relationship insights generation.
- Added unit tests for readiness score artifact generation, domain scoring output, and priority action emission.
- Added `NextBestActionsStage` to generate `Export/Reports/NEXT_BEST_ACTIONS.json` and `Export/Reports/NEXT_BEST_ACTIONS.md`, combining readiness signals, fallback pressure, unresolved relationships, and issue hotspots into one prioritized action backlog.
- Registered next-best-actions generation in the export pipeline after readiness scoring.
- Added unit tests for next-best-actions artifact generation and prioritized action categories.
- Fixed a compile-time regression in `NextBestActionsStage` by correcting `IReadOnlyList` cardinality usage (`Count` instead of `Length`) for unresolved-target handling.
- Fixed Windows/VS2022 UI build regressions by adding missing `System.IO` usage imports in `JsonExporterSettingsStore` and disambiguating WPF dispatcher calls via `System.Windows.Application.Current` in `MainWindowViewModel`.
- Added `RuntimeTypeCatalogStage` to generate `Export/Reports/RUNTIME_TYPE_CATALOG.json` and `Export/Reports/RUNTIME_TYPE_CATALOG.md` with runtime type frequencies, TIA version context, typed/fallback mapping status, and extractor suggestion text.
- Registered runtime type catalog generation in the export pipeline to support version-aware mapping expansion work.
- Added unit tests for runtime type catalog artifact generation and version-aware catalog payload validation.
- Added `TypedExtractorBacklogStage` to generate `Export/Reports/TYPED_EXTRACTOR_BACKLOG.json` and `Export/Reports/TYPED_EXTRACTOR_BACKLOG.md`, prioritizing runtime-type mapping work by fallback frequency, unresolved relationships, and extraction confidence.
- Registered typed-extractor backlog generation in the export pipeline after runtime type catalog generation.
- Added unit tests for typed-extractor backlog artifact generation and prioritized impact scoring output.
- Added `MappingImplementationTrackerStage` to generate `Export/Reports/MAPPING_IMPLEMENTATION_TRACKER.json` and `Export/Reports/MAPPING_IMPLEMENTATION_TRACKER.md`, including snapshot metrics and trend deltas across runs.
- Added persistent tracker history (`Export/Reports/MAPPING_IMPLEMENTATION_TRACKER_HISTORY.json`) to compare mapping completion progress between consecutive exports.
- Registered mapping tracker generation in the export pipeline after typed-extractor backlog generation.
- Added unit tests for mapping implementation tracker artifact generation and cross-run trend detection behavior.
- Added a root `README.md` with detailed purpose, architecture, prerequisites, Windows/VS2022 usage flow, output artifacts, configuration, reliability model, and limitations.
- Added explicit documentation maintenance policy to keep `README.md` synchronized with user-visible functionality changes.
- Added `DomainExtractorCoverageStage` to generate `Export/Reports/DOMAIN_EXTRACTOR_COVERAGE.json` and `Export/Reports/DOMAIN_EXTRACTOR_COVERAGE.md` with domain-to-extractor mapping matrix and explicit runtime-type gap list.
- Registered domain extractor coverage generation in the export pipeline after mapping tracker generation.
- Added unit tests for domain extractor coverage artifact generation and gap detection output.
- Updated `README.md` to document the new domain extractor coverage artifacts and capability.
- Added `ExecutiveSummaryStage` to generate `Export/EXECUTIVE_SUMMARY.md` and `Export/Reports/EXECUTIVE_SUMMARY.json`, consolidating run health, domain distribution, priority actions, and key artifacts.
- Fixed `ExecutiveSummaryStage` JSON payload naming collision in anonymous totals object (`ResultCount`/`IssueCount`) and aligned tests.
- Improved relationship target resolution by introducing shared normalization/resolution logic across dependency/readiness/insight/backlog/action stages.
- Aligned dependency graph test expectations with normalized target-resolution semantics (resolved edge threshold adjusted to match actual fixture graph).
- Expanded typed extraction coverage with new `MetadataDomainExtractor` and `HmiConnectionArchiveDomainExtractor` plus registration and unit tests.
- Added Windows validation workflow assets: `docs/WINDOWS_VALIDATION.md` and `scripts/validate-windows.ps1` for repeatable VS2022/TIA validation passes.
- Applied performance-oriented parsing refinements by reusing static separator buffers in high-frequency analysis stages.
- Updated `README.md` to document executive summary artifacts and Windows validation workflow assets.
- Hardened Windows TIA installation discovery for V18/V19/V20 by probing multiple Siemens registry key layouts, user/machine hives, and uninstall entries, plus recursive Siemens tree fallback probing.
- Added README troubleshooting guidance for Windows installations not being detected.

## Known Issues

- TIA Portal Openness assemblies are Windows-only and cannot be executed in the current Linux workspace.
- WPF build/runtime verification still needs confirmation on a Windows machine with the Windows Desktop workload installed.
- WPF cancellation behavior is implemented but still requires Windows runtime validation against real long-running export stages.
- Recent output folder history behavior still needs UX validation on Windows for long path editing and combobox interaction.
- Registry-based TIA installation detection currently uses best-effort value probing and needs validation against real customer installations.
- Runtime reflection signatures may vary across TIA versions; project open/device enumeration behavior requires validation on real V18/V19/V20 Windows installations.
- Block call relationships currently depend on inventory metadata (`Calls`) and still need deep Siemens block-reference extraction from real PLC software objects.
- Dependency relationships currently derive from exported metadata keys and still need deeper Siemens API relationship extraction for complete graph accuracy.
- Tag usage and unused-object detection currently rely on metadata heuristics and still require deeper Siemens semantic references for higher precision.
- Multilingual extraction currently relies on metadata key heuristics and should be extended with direct Siemens language-resource APIs when full traversal is available.
- ZIP packaging has Linux test coverage but still needs end-to-end Windows validation with real large TIA exports.
- Current Linux workspace has SDK `8.0.129`; with `global.json` pinned to `8.0.423`, local `dotnet` commands now require installing SDK `8.0.423` first.
- In this sandbox, running tests with the locally installed `~/.dotnet` SDK can fail due MSBuild named-pipe permission restrictions; verify test pass on a normal host shell/session.
- Reflection traversal now includes breadth/depth-limited graph walking; real-world validation on V18/V19/V20 projects is still required to tune false positives/duplicates and object classification heuristics.
- Typed domain extractor coverage is still partial and must be expanded domain-by-domain (Network, Hardware modules, Technology objects, Libraries, Diagnostics, Users/Audit, full HMI internals) to reach maximum-possible export completeness.
- Coverage matrix statuses are currently candidate-level heuristics and should be mapped to explicit Siemens API capability checks for final production completeness auditing.
- Network/hardware extractor classification is currently heuristic by runtime type names and should be hardened against real V18/V19/V20 runtime type catalogs.
- Library/diagnostics/users-audit extraction is currently heuristic by runtime type names and should be validated against real Siemens Openness type hierarchies per TIA version.
- Technology/HMI-deep extractors are currently heuristic by runtime type names and should be validated/tuned against real WinCC and technology object models in V18/V19/V20.
- Fallback extraction now applies coarse domain-aware classification, but mappings are still heuristic and should be refined with real Siemens runtime catalogs per version.
- Fallback hotspot analytics are currently reflection-metadata based and still require cross-validation against real Siemens API type catalogs on V18/V19/V20.
- Function-block call graph, dependency graph, and unused-object detection are still pending and currently represented by placeholder/limited reports.
- PLC extractor relationship metadata currently uses reflection heuristics/property-name conventions and still needs validation against concrete Siemens Openness block source/reference APIs.
- Dependency and call graph resolution currently matches targets by names/paths heuristically and still needs direct Siemens identifier linking for full accuracy at scale.
- Relationship insight guidance is currently heuristic and should be augmented with Siemens-native reference IDs and block compilation context when available.
- Readiness scoring weights are currently heuristic and should be calibrated using real large TIA projects (V18/V19/V20) to align with production export quality expectations.
- Next-best-actions impact scoring is currently heuristic; thresholds/weights should be tuned against real project outcomes and validated with domain experts.
- Runtime type catalog currently infers a single TIA version context from runtime metadata and should be extended for multi-version side-by-side analysis when multiple runtimes are observed.
- Typed-extractor backlog impact scoring is heuristic and should be tuned against real export outcomes and maintainer feedback to improve prioritization precision.
- Mapping trend tracking currently uses output-folder local history files and should be consolidated with a stronger run-identity model for team/CI aggregation scenarios.
- Extractor coverage status currently relies on runtime metadata heuristics and should be cross-checked against explicit extractor-registration introspection for stricter completeness reporting.
- Relationship target resolution is stronger than before but still ultimately heuristic until Siemens-native stable IDs are extracted directly from Openness object references.

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
