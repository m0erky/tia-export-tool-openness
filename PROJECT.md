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
- `TiaProjectExporter.OpennessHost`
  - Dedicated out-of-process Siemens runtime host targeting .NET Framework 4.8 to isolate Openness assembly loading from the .NET 8 UI process.
  - Invoked by `TiaProjectExporter.Tia` via process boundary and JSON payload exchange.
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

Version baseline for this milestone: **0.0.53**

Scope:

- Define TIA project traversal contracts that isolate Siemens Openness objects from the rest of the system.
- Add the first inventory/export use case for project metadata, device tree, and software object enumeration.
- Expand generated reports from placeholder repository files to real discovered content.
- Prepare the pipeline for per-object exporters and resilient export reporting.

## Current State Snapshot (2026-07-29)

### Version / Commit
- Version: `0.0.53`
- Last commit: `see git log --oneline`
- Branch: `main`

### Goal Right Now
- Stable, production-grade export of TIA projects with:
  - reliable scan/preselection workflow
  - full export of PLC/HMI/project content
  - robust handling for large projects (no crashes/OOM)

### What works
- Out-of-process Openness host (`net48`) is integrated and used by UI.
- Health check + heartbeat pipeline is implemented and visible in UI.
- Export pipeline runs resiliently stage-by-stage.
- Bundle-based inventory output exists (`Export/<Domain>/Bundles/...`).
- Pre-scan + domain selection workflow exists in UI (`Scan Project Contents`).
- Full export now forwards selected domains into host traversal for domain-aware export scope reduction.

### Current critical issue
- Hardware/domain quality is still below target on large projects when many runtime nodes arrive as fallback-only objects; confidence and semantic richness are not yet fully production-grade.
- Deep export XML failures for inconsistent blocks/UDTs (Siemens-side project consistency state) remain a key source of partial content and need clear operator guidance.

### Recent technical changes (latest)
- Hardened pre-traversal Safety login scan to prevent memory blowups on large projects:
  - bounded node scan, queue size, scan depth, and children-per-node limits
  - bounded number of per-node failure issues with explicit truncation diagnostics
  - summary diagnostics now include safety-scan limit/cap counters (`queue drops`, `enqueued nodes`, limits used)
- This specifically targets host crash scenarios (`OutOfMemoryException`) that occurred before main traversal when Safety offline login probing was enabled.
- AWL reconstruction added to the existing ByName source-reconstruction flow for `FB/FC/OB`:
  - language-aware mode selection via `Language/ProgrammingLanguage` metadata (`AWL`/`STL` -> AWL mode)
  - instruction/operator + operand stream reconstruction from `StructuredText` token stream
  - `Access -> Symbol -> Component` operand path resolution to dot-path notation
  - comments (`LineComment/Text`), blank/newline formatting, and XML entity decoding preserved
- Reconstruction summary now includes AWL-specific KPIs (`AWLEligible`, `AWLSuccess`, `AWLFailure`, `AWLNoSource`) in `EXPORT_REPORT.md`.
- Added AWL-focused tests:
  - unit tests for AWL instruction reconstruction, Access path resolution, entity decoding
  - integration test for AWL ByName JSON (`reconstructionStatus=Success`, non-empty `reconstructedSourceText`).
- Added `StructuredTextReconstructor` for `FB/FC/OB` ByName export payloads to reconstruct readable code from `exportXml` (`StructuredText`) when raw `sourceText` is only comments/whitespace.
- ByName JSON payloads now include `reconstructedSourceText`, `reconstructionStatus`, and `reconstructionDiagnostics` for `FB/FC/OB` blocks while keeping `sourceText`/`exportXml` unchanged.
- `EXPORT_REPORT.md` now includes `StructuredText Reconstruction Summary` (blocks with `exportXml`, success, `NoStructuredText`, errors, success rate).
- Added unit tests for structured-text reconstruction (IF/THEN/END_IF, access dot-path, entity decoding, missing structured text).
- Added integration coverage asserting at least one FB ByName JSON contains `reconstructionStatus=Success` and non-empty `reconstructedSourceText`.
- Safety login hardening: host now resolves multiple SafetyAdministration runtime type candidates plus derived safety-administration runtime types from loaded Siemens assemblies.
- Safety retry diagnostics: protected-block XML retry path now captures detailed login diagnostics (`SafetyLoginDiagnostics`) and reports whether no service was found vs. login attempts failed.
- Safety permission detection now also recognizes broader/localized permission-denied patterns (for example `permission denied`, `access denied`, `nicht zulässig`, `Zugriff verweigert`).
- Added optional Safety offline password flow end-to-end (UI -> export options -> inventory provider -> out-of-process host) to support SafetyAdministration login for protected block export scenarios.
- Out-of-process adapter now forwards safety password via process environment (`TIA_EXPORTER_SAFETY_OFFLINE_PASSWORD`) to avoid command-line plaintext exposure.
- Openness host now attempts `LoginToSafetyOfflineProgram(SecureString)` at project level (full export) and retries login around safety permission errors during per-node deep XML export.
- Added safety retry diagnostics in host issues/metadata (`SafetyAdministration` scope, `SafetyLoginRetrySucceeded`) to make permission behavior visible in reports.
- Added regression tests for forwarding safety password through inventory/stage layers and kept existing pipeline tests green.
- Added shared report-domain catalog (`ReportDomainCatalog`) so `EXPORT_COVERAGE_MATRIX`, `EXPORT_READINESS_SCORE`, and `NEXT_BEST_ACTIONS` now compute discovered counts from one common domain mapping.
- Added path/type-aware domain inference to reduce `Unknown` classification in `DOMAIN_EXTRACTOR_COVERAGE` and improve consistency for fallback objects.
- Extended hardware typed extraction coverage:
  - `HardwareDomainExtractor` now maps `DeviceItemImpl`, `HwIdentifier`, and `Address` runtime nodes.
  - hardware metadata enrichment now includes `HardwareIdentifier` and `Address` when exposed by runtime objects.
- Added traversal guard in out-of-process host to skip likely recursive hardware path expansion patterns (for example repeated `DeviceItemImpl` chains), reducing structural noise and memory churn risks.
- Improved tag-usage analysis:
  - `ObjectUsageAnalysisStage` now combines dependency metadata with deep-content text references (`ExportXmlContent`, `SourceTextContent`, etc.).
  - known tag identifiers referenced in XML/source are now counted as usage edges.
- Hardened PLC data type discovery in reporting by treating type-path/runtime-type signals (`/TypeGroup`, `/Types`, `PlcStruct`, `UserDataType`, etc.) as `PLC.DataTypes` domain evidence.
- Added tests for:
  - cross-report domain discovered-count consistency (`Coverage`/`Readiness`/`NextBestActions`)
  - hardware extractor support for `DeviceItemImpl`/`HwIdentifier`/`Address`
  - tag usage extraction from export XML content
  - reduced `Unknown` rows in domain extractor coverage output.
- Added shared call-relationship extractor (`CallRelationshipExtractor`) for analysis stages:
  - parses call targets from metadata (`Calls`, `BlockCalls`, `InvokedBlocks`, `CalledBlocks`)
  - additionally parses `<CallInfo Name="...">` from `Content.ExportXml`
  - extracts instance references from `<Component Name="...">` and emits mapped call relations.
- Added instance DB target mapping (`InstanceOfName`/`InstanceOf`/`DataType`) so calls referencing instance DB names resolve to FB targets for graph/dependency consistency.
- Updated `BlockCallGraphStage`, `DependencyGraphStage`, and `RelationshipInsightsStage` to use shared XML-backed call extraction.
- Added tests for:
  - OB export-XML call parsing (`Call edges: 2` scenario)
  - instance DB target mapping to FB targets in dependencies
  - relationship insights edge generation from XML call info.
- Added centralized qualified-path canonicalization utility (`QualifiedPathCanonicalizer`) for stable path normalization across inventory processing.
- Added centralized inventory deduplication utility (`TiaInventoryDeduplicator`) using key `(ObjectType, CanonicalQualifiedPath)` with conflict rule:
  - typed extraction > host plc model > reflection
  - then richer content (`Content.ExportXml`/`Content.SourceText`).
- Integrated deduplication directly into `ProjectInventoryStage` before downstream stages, so call graph/dependency/tag usage/readiness/coverage all run on deduplicated canonical inventory.
- Added per-block single-object export artifacts (backward-compatible add-on):
  - `Export/Blocks/ByName/<Type>_<Name>.json`
  - `Export/Blocks/ByName/<Type>_<Name>.md`
  - optional XML when XML format enabled
  - `Export/Blocks/ByName/INDEX.json` with type/name/number/file/canonicalPath.
- Extended export reporting with explicit deduplication summary sections (input, removed duplicates, unique objects, top duplicate groups, conflict rule).
- Added end-to-end domain-aware traversal forwarding for full exports:
  - UI-selected `IncludedDomains` now flow through inventory provider and openness adapter into host CLI argument `--domains`.
  - host parses and applies traversal scope restrictions before expensive traversal branches.
- Added traversal scope gating in host:
  - PLC-focused traversal runs only when PLC-related domains are selected.
  - generic software graph traversal is skipped for PLC-only selections (for example Blocks/Tags/UDTs), reducing unnecessary whole-project traversal.
- Added/updated tests for forwarding semantics:
  - inventory provider tests now verify selected domains are forwarded with full traversal and preview remains unscoped by default.
- Preview scan now records root-level diagnostics counters in project metadata:
  - discovered PLC entry points
  - discovered `BlockGroup` count
  - discovered `OB`/`FB`/`FC`/`DB` counts
  - fallback activations, fallback nodes visited, preview limit hits.
- Added targeted preview fallback for block discovery:
  - if normal preview walk finds no blocks for an entry point, run bounded PLC reflection fallback focused on block-relevant runtime paths.
  - fallback is depth/node capped (`MaxPreviewFallbackDepth=5`, `MaxPreviewFallbackNodes=240`) to protect performance/memory on large projects.
- Added provider-level test coverage to ensure preview/full traversal detail-level forwarding remains correct (`Preview` vs `Full`).

### Critical files
- `src/TiaProjectExporter.OpennessHost/Program.cs`
- `src/TiaProjectExporter.Tia/Inventory/OutOfProcessTiaProjectOpennessAdapter.cs`
- `src/TiaProjectExporter.Tia/Inventory/OpennessBackedTiaProjectInventoryProvider.cs`
- `src/TiaProjectExporter.Core/Models/TiaTraversalDetailLevel.cs`
- `src/TiaProjectExporter.UI/ViewModels/MainWindowViewModel.cs`
- `src/TiaProjectExporter.Core/Models/TiaInventoryDomainClassifier.cs`
- `src/TiaProjectExporter.Export/Stages/ProjectInventoryStage.cs`
- `src/TiaProjectExporter.Export/Stages/InventoryObjectExportStage.cs`

### Open bugs / risks
- Preview still depends on runtime-type heuristics in fallback traversal and can miss rare proprietary object patterns.
- Preview diagnostics are currently metadata-only and should be surfaced more explicitly in Windows UI logs for field support.
- Full export quality still depends on typed extraction depth + robust fallback handling.

### Decisions (do not change without reason)
- Keep out-of-process host model (stability and assembly isolation).
- Keep two-phase workflow (pre-scan then export).
- Keep full export independent from preview object counts.
- Keep bundle-first output structure for large projects.

### Next 3 steps (priority)
1. Finish high-volume hardware typed mappings beyond `DeviceItemImpl`/`HwIdentifier`/`Address` to reduce fallback-only hardware nodes in real projects.
2. Add Siemens-consistency remediation guidance in report/UI for "Inconsistent blocks and PLC data types (UDT) cannot be exported" failures.
3. Validate Windows full-export runs on large projects and tune traversal limits/guards using measured memory/time baselines.

### Validation checklist
- [x] `dotnet test tests/TiaProjectExporter.Tests/TiaProjectExporter.Tests.csproj --no-restore -v minimal`
- [x] `dotnet build src/TiaProjectExporter.OpennessHost/TiaProjectExporter.OpennessHost.csproj -v minimal`
- [x] Dedup unit tests cover canonicalization patterns + conflict-resolution priorities.
- [x] Per-block export tests cover by-name artifacts + index generation.
- [x] XML call parsing tests cover OB `<CallInfo>` extraction and instance mapping.
- [x] Report domain discovered counts are consistent across Coverage/Readiness/NextBestActions.
- [x] Tag usage detects XML/source references for known tags.
- [x] Hardware typed extraction includes `DeviceItemImpl`/`HwIdentifier`/`Address`.
- [ ] UI: `Scan Project Contents` shows non-zero Blocks for known project with FB/FC/DB.
- [ ] UI: Export runs after scan with default selections.
- [ ] UI: Single-domain export (for example only `Blocks`) does not traverse unrelated domains in host runtime logs.
- [ ] Export output contains block bundles and source/xml content where available.
- [ ] No host crash/OOM during scan or export.

### Handy runtime logs
- Host stderr logs:
  - `%LocalAppData%\\TiaProjectExporter\\HostLogs\\host-stderr-*.log`
- Crash logs:
  - `%LocalAppData%\\TiaProjectExporter\\CrashLogs\\crash-*.log`

## TODO List

- Implement deep Siemens.Engineering traversal (devices, network, PLC software, blocks, tags, UDTs, HMI, diagnostics) behind the reflection-safe Openness adapter.
- Add Siemens-API-backed relationship extraction for block calls, tag usage, and dependencies to replace remaining metadata heuristics.
- Expand typed extractor mappings for remaining high-volume runtime nodes currently emitted as `UnmappedRuntimeNode` fallback.
- Validate/extend PLC extractor mappings for instance DBs, block language metadata, and tag-table semantics against real TIA V18/V19/V20 runtime objects.
- Validate WPF build and runtime behavior on a Windows machine with the .NET 8 SDK installed.
- Validate Windows registry detection against actual TIA V18/V19/V20 installations and adjust key/value probing as needed.
- Add optional auto-validation trigger when manual TIA override path changes (currently explicit Validate button).
- Add folder-based project picker support for `.ap18/.ap19/.ap20` directory-style projects in addition to file picker.
- Validate SafetyAdministration login flow on a Windows machine with real safety-enabled project/password combinations and tune service-type probing if needed.

## Completed Tasks

- Added OOM-guard rails for Safety pre-login probing in Openness host and improved diagnostics for bounded scans.
- Incremented application version to `0.0.53` in central build metadata and UI fallback version resolution.
- Extended ByName source reconstruction to support AWL/STL language blocks in addition to existing SCL/structured-text behavior.
- Added AWL-specific reconstruction KPIs to report summary output.
- Added AWL unit + integration coverage without changing backward-compatible `sourceText`/`exportXml` fields.
- Incremented application version to `0.0.52` in central build metadata and UI fallback version resolution.
- Implemented structured-text reconstruction for ByName `FB/FC/OB` exports and exposed reconstruction fields in per-block JSON artifacts.
- Added `StructuredText Reconstruction Summary` section to export report output.
- Added unit + integration tests for reconstruction behavior and ByName payload population.
- Incremented application version to `0.0.51` in central build metadata and UI fallback version resolution.
- Hardened host SafetyAdministration service-type resolution with fallback candidates and runtime-derived matching.
- Improved safety retry diagnostics in deep XML export handling (node-level login diagnostics and clearer failure reporting).
- Extended safety permission-denied detection with additional localized/wording variants.
- Incremented application version to `0.0.50` in central build metadata and UI fallback version resolution.
- Added optional Safety offline password setting in WPF UI and wired secure input handling via `PasswordBox` event forwarding to the ViewModel.
- Extended export contracts (`ExportOptions`, `ITiaProjectInventoryProvider`, `ITiaProjectOpennessAdapter`) to carry optional safety password through the pipeline.
- Implemented host-side safety login and retry behavior for safety permission-denied deep export cases.
- Extended tests to verify safety password forwarding in project inventory stage/provider flow.

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
- Refined discovery filters to reject non-product uninstall matches (for example `TIA Portal Help Viewer`) and prefer likely real portal installations with Openness indicators.
- Added optional manual installation override plumbing (`TiaInstallationPathOverride`) across UI options, export options, inventory provider, and Openness adapter contracts.
- Extended the WPF settings panel and persisted user settings with `TIA Installation Path Override (optional)` so users can export even when registry discovery misses custom installs.
- Added/updated test coverage for new override-aware signatures and adapter forwarding behavior.
- Added WPF folder browsing and explicit `Validate Path` flow for manual TIA override selection.
- Added V20+Openness override validation feedback in UI based on V20 path heuristics and `Siemens.Engineering.dll` discovery.
- Extracted shared Openness runtime assembly path resolution into `OpennessRuntimeLocator` and reused it in adapter/UI validation.
- Fixed `OpennessRuntimeLocator` candidate path handling to be OS-agnostic (segment-based) so `PublicAPI/V20` discovery works reliably in tests and mixed path separator environments.
- Fixed WPF build break in `MainWindowViewModel` by importing `System.IO` for `Directory` usage in manual override validation.
- Added source project path UX improvements: `Browse` and `Validate Project` actions plus explicit UI validation feedback.
- Hardened export start conditions to require valid existing `.ap18/.ap19/.ap20` project paths before running pipeline stages.
- Updated UI export completion status logic to report "completed with issues" whenever issues are present, even with zero failed stages.
- Changed inventory-stage result mapping so `TiaInventoryStatus.Unavailable` contributes a failed result instead of skipped.
- Added WPF app-level exception hardening (startup/exit guards and unhandled exception handlers) to reduce unexpected shutdowns.
- Updated unit tests to assert failed inventory result behavior when inventory is unavailable.
- Fixed Windows build ambiguity by fully qualifying WPF `MessageBox` and `Microsoft.Win32.OpenFileDialog` after enabling Windows Forms folder picker support.
- Improved Openness traversal diagnostics by unwrapping nested reflection invocation exceptions into export issue details.
- Updated inventory status heuristics so "root-only object + traversal issues" is classified as `Unavailable` rather than `Partial`.
- Added test coverage for the root-only+issue inventory classification case.
- Updated partial-inventory unit fixture to include root + device objects so it remains classified as `Partial` under the new root-only failure heuristic.
- Updated the corresponding assertion in partial-inventory unit tests to expect two objects (root + device) after fixture expansion.
- Added persistent UI log snapshot storage in `UiLogCollector` so failure diagnostics can include prior runtime log context.
- Added export/command failure diagnostics file output to `Export/Reports/EXPORT_FAILURE.log` with stack trace and runtime context.
- Added app-level crash file logging under `%LocalAppData%/TiaProjectExporter/CrashLogs` for unhandled UI/startup/task exceptions.
- Incremented application version to `0.0.2` in central build metadata and version fallback logic.
- Added explicit UI runtime identifier targeting (`win-x64`) to prevent `NETSDK1047` during self-contained Windows publish.
- Incremented application version to `0.0.3` in central build metadata.
- Added a dedicated out-of-process Siemens Openness host project (`TiaProjectExporter.OpennessHost`, net48) to execute runtime traversal in a CLR-compatible process.
- Implemented `OutOfProcessTiaProjectOpennessAdapter` and switched DI registration to host-process traversal by default.
- Added host executable discovery (`TIA_EXPORTER_OPENNESS_HOST_PATH` env var or colocated host exe) with explicit diagnostics when host deployment is missing.
- Added packaging integration so UI build/publish copies `TiaProjectExporter.OpennessHost.exe` and `.config` into output directories.
- Added Linux-safe unit test coverage for out-of-process adapter non-Windows behavior.
- Incremented application version to `0.0.4` in central build metadata and UI fallback version resolution.
- Added dedicated Openness health-check service (`IOpennessHealthCheckService`) with host-process validation and detailed diagnostics.
- Extended Openness host with `--health` execution mode to verify Siemens assembly loadability without full traversal.
- Added UI `Health Check` command plus traffic-light indicator (green/yellow/red) and status text.
- Added Linux-safe unit test coverage for out-of-process health-check service non-Windows behavior.
- Incremented application version to `0.0.5` in central build metadata and UI fallback version resolution.
- Fixed .NET Framework 4.8 host build compatibility by replacing `init` accessors with `set` accessors in host options (`IsExternalInit` no longer required).
- Incremented application version to `0.0.6` in central build metadata and UI fallback version resolution.
- Fixed additional net48 host compile issues (generic JSON serializer target, nullable assignment guards, and list conversion mismatches).
- Reworked UI build orchestration to invoke host build via MSBuild target instead of cross-TFM project reference to avoid NU1702 compatibility warning.
- Documented .NET Framework 4.8 Developer Pack prerequisite for Windows builds.
- Incremented application version to `0.0.7` in central build metadata and UI fallback version resolution.
- Added export preflight validation for writable output directories (active probe file create/delete) to prevent silent no-output runs.
- Added automatic pre-export Openness health-check gate in UI; export now aborts early when health state is `Unhealthy`.
- Added warning-state handling for missing `Siemens.Engineering.Contract.dll` in health checks (amber status instead of green).
- Added fallback failure diagnostics path `%LocalAppData%/TiaProjectExporter/FailureDiagnostics` when output-folder diagnostic write fails.
- Incremented application version to `0.0.8` in central build metadata and UI fallback version resolution.
- Added host heartbeat streaming from out-of-process host via stderr (`HB|...`) and adapter-side parsing/log forwarding.
- Added dedicated UI host-activity liveness indicator (traffic light + heartbeat age text) independent from stage-level progress updates.
- Implemented heartbeat timeout thresholds for liveness display (`<=15s` green, `16-60s` yellow, `>60s` red).
- Incremented application version to `0.0.9` in central build metadata and UI fallback version resolution.
- Added persistent out-of-process host stderr transcript logging under `%LocalAppData%/TiaProjectExporter/HostLogs` for post-mortem diagnostics even when UI logs are sparse.
- Incremented application version to `0.0.10` in central build metadata and UI fallback version resolution.
- Integrated Siemens `Siemens.Collaboration.Net.TiaPortal.Openness.Extensions` (`20.0.1744193700`) into the net48 out-of-process host to use Siemens-standard resolver initialization before first Openness access.
- Added robust resolver bootstrap in `TiaProjectExporter.OpennessHost` (`Api.Global().Openness().Initialize()` via reflection), with explicit diagnostic messages and fallback to manual assembly loading.
- Incremented application version to `0.0.11` in central build metadata and UI fallback version resolution.
- Replaced floating central Siemens package version (`20.*`) with fixed version `20.0.1744193700` to satisfy NuGet central package management constraints (`NU1011`).
- Incremented application version to `0.0.12` in central build metadata and UI fallback version resolution.
- Changed host build behavior from full suppression to warning-visible handling for `NU1603` via `WarningsNotAsErrors`, so Siemens dependency resolution mismatches remain transparent but no longer fail the full solution restore.
- Incremented application version to `0.0.14` in central build metadata and UI fallback version resolution.
- Hardened host traversal against deep-node stalls by adding candidate-property filtering, per-node child limits, per-enumerable item limits, and slow-property diagnostics in `TiaProjectExporter.OpennessHost` reflection walk.
- Added detailed heartbeat phase transitions for property-level traversal (`TraverseProperty`) to make long-running/stuck runtime members diagnosable.
- Incremented application version to `0.0.15` in central build metadata and UI fallback version resolution.
- Fixed out-of-process host response deserialization for `metadata` by supporting both object-shaped JSON and DataContract dictionary-entry array shape (`Key`/`Value`).
- Prevented inventory aborts caused by `System.Text.Json.JsonException` at `$.objects[*].metadata` when host emits DataContract-style dictionary payloads.
- Incremented application version to `0.0.16` in central build metadata and UI fallback version resolution.
- Fixed host deployment packaging by copying the complete `TiaProjectExporter.OpennessHost` net48 output set (not only `.exe`/`.config`) into UI build/publish outputs.
- Resolved runtime `FileNotFoundException` for Siemens collaboration dependencies (for example `Siemens.Collaboration.Net.CoreExtensions`) when launching out-of-process host from deployed UI folder.
- Incremented application version to `0.0.17` in central build metadata and UI fallback version resolution.
- Added `InventoryObjectExportStage` to emit per-inventory-object artifacts (`.json`, `.xml`, `.md`) into domain folders under `Export/<Domain>/Objects/...`.
- Wired object-export stage into the main export pipeline directly after inventory collection.
- Added unit test coverage for per-object artifact generation and domain-folder routing.
- Incremented application version to `0.0.18` in central build metadata and UI fallback version resolution.
- Fixed `InventoryObjectExportStageTests` assertion to use `ExportedObjectResult.ObjectType` (instead of non-existent `Scope`) for compatibility with current domain model.
- Incremented application version to `0.0.19` in central build metadata and UI fallback version resolution.
- Refined inventory object domain routing precedence to classify hardware (`Device/Module/Rack/Cpu`) before block heuristics and avoid false `Blocks` mapping for names like `PLC_1`.
- Narrowed block-domain keyword matching to reduce substring collisions.
- Incremented application version to `0.0.20` in central build metadata and UI fallback version resolution.
- Fixed UI→host build orchestration so the net48 `TiaProjectExporter.OpennessHost` build does not inherit `win-x64` runtime settings from UI publish/build; resolves `NETSDK1047` (`net48/win-x64`) during Windows publish.
- Documented optional `TiaPortalLocation` publish property for Siemens PublicAPI reference resolution warnings.
- Incremented application version to `0.0.21` in central build metadata and UI fallback version resolution.
- Added dedicated PLC-focused host traversal pass that explicitly targets software/block/tag/datatype candidate properties to improve runtime discovery of PLC assets beyond generic hardware graph traversal.
- Added PLC-aware object classification in host (`OB`, `FB`, `FC`, `DB`, `InstanceDB`, `Tag`, `TagTable`, `UDT`, `TechnologyObject`, `Source`) to improve downstream domain routing and export usefulness.
- Incremented application version to `0.0.22` in central build metadata and UI fallback version resolution.
- Added bounded scalar metadata extraction (`Prop.*`) per discovered runtime node in host traversal to capture more settings/configuration fields in exported object artifacts.
- Added metadata safeguards (entry/value limits and slow-property diagnostics) to balance depth with stability on large projects.
- Incremented application version to `0.0.23` in central build metadata and UI fallback version resolution.
- Added Siemens service-based PLC entry-point probing (`GetService(...)` with software-container candidates) in host traversal so PLC software trees can be discovered even when not surfaced as direct object properties.
- Added bounded service-probe breadth/depth with heartbeat phase reporting (`TraversePlcServiceProbe`) to keep diagnostics clear during PLC discovery.
- Incremented application version to `0.0.24` in central build metadata and UI fallback version resolution.
- Added deep-content extraction in host traversal for software/runtime nodes:
  - attempts reflective `Export(FileInfo|string)` and captures returned XML content as `Content.ExportXml`
  - probes source-like properties (`Source`, `Text`, `Code`, `SclSource`, `ExternalSource`) as `Content.SourceText`
- Extended object export stage to emit deep-content sidecar files per object (`*.content.export.xml`, `*.content.source.md|txt`).
- Added test coverage updates for deep-content sidecar artifact generation.
- Incremented application version to `0.0.25` in central build metadata and UI fallback version resolution.
- Fixed inventory object domain routing precedence to classify explicit PLC object types (`FB/FC/OB/DB/InstanceDB`, `Tag/TagTable`, `UDT`) by `ObjectType` before path keywords, preventing `Blocks` objects from being misrouted to `Hardware` when paths contain `Devices/...`.
- Incremented application version to `0.0.26` in central build metadata and UI fallback version resolution.
- Fixed net48 compatibility regressions in `TiaProjectExporter.OpennessHost` by removing `Index/Range` syntax and adjusting reflection argument typing (`object[]`) for `Export(...)` invocation.
- Incremented application version to `0.0.27` in central build metadata and UI fallback version resolution.
- Switched inventory object serialization to content-first compact metadata: removed noisy `Prop.*` and raw `Content.*` payloads from primary JSON/XML/Markdown object files while keeping deep-content sidecar artifacts.
- Incremented application version to `0.0.28` in central build metadata and UI fallback version resolution.
- Reworked inventory object export from per-object files to bundle-first files grouped by domain/object type (`Export/<Domain>/Bundles/<ObjectType>.json|xml|md`) to reduce artifact explosion for large projects.
- Bundle artifacts now carry deeper payloads (including source and export XML content sections) so fewer files still retain actionable engineering content.
- Updated unit tests for bundle-based output assertions.
- Incremented application version to `0.0.29` in central build metadata and UI fallback version resolution.
- Fixed duplicate-key crashes in `TypedExtractorBacklogStage` and `ObjectUsageAnalysisStage` by deduplicating/grouping repeated inventory IDs/paths before dictionary creation.
- Incremented application version to `0.0.30` in central build metadata and UI fallback version resolution.
- Extended host deep-content extraction with source-oriented method probing (`GenerateSource`, `GetSource`, `GetText`, etc.) before XML fallback.
- Added XML content parsing fallback for source-like elements (`Source`, `StatementList`, `Implementation`, `Code`, etc.) to derive readable code text when direct source fields are not exposed.
- Incremented application version to `0.0.31` in central build metadata and UI fallback version resolution.
- Added centralized semantic version metadata in `Directory.Build.props` and set initial released version to `0.0.1`.
- Exposed application version in WPF UI (`WindowTitle` and header version text) based on assembly informational version.
- Hardened deep content export according to Siemens Openness export/import guidance by trying multiple `Export(...)` overloads and preferring `FileInfo + ExportOptions.WithDefaults` when available.
- Extended PLC model traversal with explicit collections (`BlockGroup`, `TagTableGroup`, `TypeGroup`, `TechnologyObjects`, `ExternalSources`, `Sources`) to improve extraction coverage of blocks/tags/types/sources.
- Tightened PLC runtime filtering to keep export bundles focused on engineering-relevant software objects and reduce low-value object noise.
- Incremented application version to `0.0.32` in central build metadata and UI fallback version resolution.
- Fixed net48 host compile issues after export-overload hardening:
  - nullable flow warnings in error/source assignment (`CS8604`, `CS8601`)
  - removed unsupported `string.Replace` overload with `StringComparison` for .NET Framework (`CS1501`)
- Incremented application version to `0.0.33` in central build metadata and UI fallback version resolution.
- Fixed heartbeat monitoring pipeline in UI:
  - ensured `UiLoggerProvider` is registered in logging configuration pipeline
  - made heartbeat parsing tolerant for both `HostHeartbeat|...` and raw `HB|...` log payloads
  - added timestamp parsing fallback to avoid stuck "Waiting for first heartbeat" state on format deviations
- Incremented application version to `0.0.34` in central build metadata and UI fallback version resolution.
- Added out-of-memory safeguards for out-of-process Openness host deep-content extraction:
  - XML export file size guard before loading content into memory
  - content truncation caps for `Content.ExportXml` and `Content.SourceText` metadata fields
  - large XML parsing guard for source-text extraction from export XML
- Incremented application version to `0.0.35` in central build metadata and UI fallback version resolution.
- Improved UI log usability with explicit scrolling support:
  - added "Jump to latest" action in log panel
  - log panel now auto-scrolls while at bottom and preserves manual inspection when user scrolls up
- Incremented application version to `0.0.36` in central build metadata and UI fallback version resolution.
- Implemented two-step selective export workflow in UI:
  - added project pre-scan command (`Scan Project Contents`) using inventory provider
  - added selectable export-domain list with discovered object counts
  - export now requires completed pre-scan and at least one selected domain
- Added domain classification utility in core and domain filter propagation through export options/stages.
- Incremented application version to `0.0.37` in central build metadata and UI fallback version resolution.
- Fixed domain-classification regression where non-root objects (for example devices under `Project/...`) were incorrectly mapped to `Project`; project-domain mapping now targets root/project nodes only.
- Incremented application version to `0.0.38` in central build metadata and UI fallback version resolution.
- Introduced traversal detail levels (`Preview` vs `Full`) for Openness traversal.
- Pre-scan now uses lightweight preview inventory (`BuildInventoryPreviewAsync`) and no longer reuses preview objects for final export traversal.
- Out-of-process host now supports `--preview` and executes a reduced top-level traversal for faster scope selection.
- Incremented application version to `0.0.39` in central build metadata and UI fallback version resolution.
- Updated test stub implementations of `ITiaProjectInventoryProvider` to implement the new preview method contract (`BuildInventoryPreviewAsync`) and restore test compilation.
- Incremented application version to `0.0.40` in central build metadata and UI fallback version resolution.
- Fixed selective-export false-negative behavior when preview scan under-detects domains (for example Blocks):
  - all domains are now selected by default after scan
  - selected-domain export filter no longer requires preview object count > 0
  - export start condition requires any selected domain (not selected+count)
- Incremented application version to `0.0.41` in central build metadata and UI fallback version resolution.
- Improved preview scan block detection by extending preview traversal with bounded PLC model child enumeration (`BlockGroup`, nested groups, tags/types/technology sources) so Blocks/Tags/UDTs are visible in selection even in lightweight mode.
- Incremented application version to `0.0.42` in central build metadata and UI fallback version resolution.
- Hardened preview scan PLC block discovery with bounded block-focused reflection fallback (`HostPreviewBlockFallback`) that activates when entry-point preview traversal yields no blocks.
- Added preview diagnostics counters persisted on root metadata (`PreviewDiagnostics.*`) for PLC entry points, block-group counts, block-type counts (OB/FB/FC/DB), fallback activity, and preview limit hits.
- Added inventory-provider tests to validate traversal detail-level forwarding (`BuildInventoryPreviewAsync` => `Preview`, `BuildInventoryAsync` => `Full`).
- Incremented application version to `0.0.44` in central build metadata and UI fallback version resolution.
- Added domain-aware full traversal filtering by forwarding selected export domains (`IncludedDomains`) through inventory provider and openness adapter into host argument `--domains`.
- Added host-side domain scope parsing and traversal gating so PLC-only selections skip unrelated generic software traversal paths.
- Extended inventory-provider tests to assert selected domain forwarding behavior for full traversal calls.
- Incremented application version to `0.0.45` in central build metadata and UI fallback version resolution.
- Added centralized qualified-path canonicalization (`QualifiedPathCanonicalizer`) and inventory deduplication (`TiaInventoryDeduplicator`) with stable `(ObjectType, CanonicalQualifiedPath)` keys.
- Added documented dedup conflict strategy (`typed > host plc model > reflection`, then richer content) and propagated dedup metadata (`CanonicalQualifiedPath`, `OriginalQualifiedPaths`) to retained nodes.
- Integrated deduplication into `ProjectInventoryStage` so all downstream analysis/report stages use canonical deduplicated data.
- Added per-block by-name exports under `Export/Blocks/ByName` with JSON/Markdown (+ optional XML) and `INDEX.json` for direct engineering lookup.
- Extended export reporting with a `Deduplication Summary` section and dedup statistics payload in `PROJECT_STATISTICS.json`.
- Added tests for path canonicalization, dedup conflict resolution, dedup stage integration, by-name file naming, and by-name index generation.
- Incremented application version to `0.0.46` in central build metadata and UI fallback version resolution.
- Added XML-backed call relationship extraction for analysis stages so `<CallInfo Name="...">` entries in block export XML contribute to calls/dependencies/insights even when runtime metadata keys are empty.
- Added instance-DB target mapping (`InstanceOfName`/`InstanceOf`/`DataType`) so call targets referencing instance DB names resolve to FB targets.
- Updated block call graph, dependency graph, and relationship insights stages to share the new call extraction logic for consistent edge counts.
- Added tests for OB XML call parsing (`Block_1`, `Block_2`), instance mapping (`Block_1_DB -> Block_1`), and relationship insights edge generation from XML.
- Incremented application version to `0.0.47` in central build metadata and UI fallback version resolution.

## Known Issues

- TIA Portal Openness assemblies are Windows-only and cannot be executed in the current Linux workspace.
- WPF build/runtime verification still needs confirmation on a Windows machine with the Windows Desktop workload installed.
- WPF cancellation behavior is implemented but still requires Windows runtime validation against real long-running export stages.
- Recent output folder history behavior still needs UX validation on Windows for long path editing and combobox interaction.
- Registry-based TIA installation detection currently uses best-effort value probing and needs validation against real customer installations.
- Manual override V20 path detection is heuristic (`V20`/`PublicAPI/V20`) and should be cross-validated against broader enterprise install layouts.
- Source project browse currently uses file picker workflow; directory-style `.apXX` project selection still relies on manual path input.
- Linux-based automated test environment still cannot validate WPF runtime crash-path handling; verify new diagnostics files on Windows runtime failures.
- Out-of-process host is currently .NET Framework 4.8 only and requires appropriate runtime/tooling on Windows build and execution hosts.
- Siemens NuGet package restore for the host now depends on access to `nuget.org` (or an internal mirrored feed) during restore/build.
- Siemens feed variability can produce transitive lower-bound resolution warnings (`NU1603`) for Openness packages; host project now keeps this warning visible while treating it as non-fatal (`WarningsNotAsErrors`) so real dependency problems are still diagnosable.
- Reflection traversal remains heuristic and can still be slow on very large runtime nodes; newly added safeguards reduce hangs but should be complemented with progressively more typed Siemens extractor implementations.
- Host/UI JSON contract still uses mixed serializer technologies (DataContractJsonSerializer in host, System.Text.Json in adapter); medium-term cleanup should unify on a single serializer for stricter compatibility guarantees.
- Host deployment still assumes side-by-side assembly loading from the UI output directory; a future installer/MSIX workflow should explicitly validate file completeness post-install.
- Domain routing for object exports is currently heuristic (type/path keyword matching) and should be tightened with typed Siemens object classification as extractor coverage increases.
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
- Automate patch-version incrementing in CI/release workflow to enforce per-commit version progression.

## Build Instructions

Expected local build commands:

```bash
dotnet restore
dotnet build TiaProjectExporter.sln
dotnet test TiaProjectExporter.sln
```

Windows publish for UI + host:

```powershell
dotnet restore src/TiaProjectExporter.UI/TiaProjectExporter.UI.csproj -r win-x64
dotnet publish src/TiaProjectExporter.UI/TiaProjectExporter.UI.csproj -c Release -r win-x64 --self-contained true
```

SDK pinning:

- `global.json` in the repository root pins the SDK to `8.0.423` (`rollForward: latestPatch`).

Windows-specific notes:

- The UI project targets WPF and should be built on Windows with the .NET 8 SDK and Windows Desktop workload available.
- TIA Openness integration requires Siemens TIA Portal installations and compatible Openness assemblies for V18/V19/V20.
- Restore/build now also requires access to Siemens NuGet packages (for `Siemens.Collaboration.Net.TiaPortal.Openness.Extensions` `20.0.1744193700`) in `TiaProjectExporter.OpennessHost`; use `nuget.org` or a mirrored internal feed.
- Linux verification command used successfully in this workspace:

```bash
DOTNET_CLI_HOME=/tmp DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 dotnet test tests/TiaProjectExporter.Tests/TiaProjectExporter.Tests.csproj
```
