# Windows Validation Workflow (VS2022 + TIA Openness)

This document defines the recommended validation pass on a Windows machine with real TIA Portal installations.

## Goal

Validate end-to-end exporter behavior against real projects for:

- TIA V18
- TIA V19
- TIA V20

## Prerequisites

- Windows 10/11
- Visual Studio 2022 with `.NET desktop development`
- .NET SDK `8.0.423`
- One or more TIA installations (V18/V19/V20) with Openness available
- Representative `.ap18/.ap19/.ap20` projects (small, medium, large)

## Validation checklist

1. Build and test
   - `dotnet restore`
   - `dotnet build TiaProjectExporter.sln -c Release`
   - `dotnet test tests/TiaProjectExporter.Tests/TiaProjectExporter.Tests.csproj -c Release`
2. UI startup
   - launch `TiaProjectExporter.UI`
   - click `Detect Versions`
   - verify discovered installations/version labels
3. Export run per version
   - run at least one export per TIA version available
   - generate JSON/XML/Markdown with compression enabled
4. Artifact verification
   - verify all expected `Export/Reports/*` files exist
   - inspect `EXECUTIVE_SUMMARY`, `EXPORT_REPORT`, `EXPORT_READINESS_SCORE`, `NEXT_BEST_ACTIONS`
5. Stability checks
   - cancel a long-running export and confirm graceful cancellation
   - re-run export to same output and confirm history/trend files update
6. Performance checks
   - measure runtime and memory on large project
   - note slow stages and dominant artifact sizes
7. Regression notes
   - log failures and sample runtime types needing new mappings

## Recommended sample matrix

- Small project (1-2 PLCs, limited HMI)
- Medium project (multi-device, richer network and HMI)
- Large project (high object count, libraries, diagnostics, technology objects)

For each sample:

- capture start/end times
- capture `Export/Reports/MAPPING_IMPLEMENTATION_TRACKER.json`
- capture `Export/Reports/DOMAIN_EXTRACTOR_COVERAGE.json`
- capture `Export/Reports/TYPED_EXTRACTOR_BACKLOG.json`

## Expected outcomes

- No crashes; failures appear as recoverable issues in reports.
- Version detection and project open should succeed where environment allows.
- Gap/backlog artifacts should identify actionable typed extractor work.
