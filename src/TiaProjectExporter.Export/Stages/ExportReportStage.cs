using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Generates execution-driven report artifacts once export stages have run.
/// </summary>
public sealed class ExportReportStage : IExportStage
{
    /// <inheritdoc />
    public string Name => "Export Reports";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var duration = generatedAt - context.StartedAt;
        var report = context.BuildReport();

        var projectOverview = BuildProjectOverviewMarkdown(context, report, duration);
        var exportReport = BuildExportReportMarkdown(context, report, duration, generatedAt);
        var projectStatistics = BuildProjectStatisticsJson(context, report, duration, generatedAt);

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/PROJECT_OVERVIEW.md", ExportFormat.Markdown, projectOverview),
                cancellationToken).ConfigureAwait(false);

            await context.WriteArtifactAsync(
                new ExportArtifact("Export/EXPORT_REPORT.md", ExportFormat.Markdown, exportReport),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/PROJECT_STATISTICS.json", ExportFormat.Json, projectStatistics),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Report", "ExecutionSummary", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Execution reports generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static string BuildProjectStatisticsJson(
        ExportExecutionContext context,
        ExportReport report,
        TimeSpan duration,
        DateTimeOffset generatedAt)
    {
        var jsonOptions = JsonOptionsFactory.CreateDefault();
        var fallbackSummary = BuildFallbackSummary(context.Inventory);
        var objectTypeCounts = report.Results
            .GroupBy(result => result.ObjectType)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var payload = new
        {
            GeneratedAt = generatedAt,
            StartedAt = report.StartedAt,
            FinishedAt = report.FinishedAt,
            DurationSeconds = Math.Round(duration.TotalSeconds, 2),
            Totals = new
            {
                Results = report.Results.Count,
                Succeeded = report.SucceededCount,
                Failed = report.FailedCount,
                Skipped = report.SkippedCount,
                Issues = report.Issues.Count
            },
            Options = new
            {
                context.Options.ProjectPath,
                context.Options.OutputDirectory,
                Formats = context.Options.Formats
                    .Select(format => format.ToString())
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray(),
                context.Options.EnableCompression,
                context.Options.SkipDiagnostics,
                context.Options.GenerateMarkdownSummaries
            },
            ObjectTypes = objectTypeCounts,
            Archive = BuildArchiveSection(context, report),
            FallbackExtraction = new
            {
                fallbackSummary.TotalObjects,
                fallbackSummary.TotalTypedObjects,
                fallbackSummary.TotalFallbackObjects,
                fallbackSummary.TotalUniqueFallbackRuntimeTypes,
                Hotspots = fallbackSummary.Hotspots
            }
        };

        return JsonSerializer.Serialize(payload, jsonOptions);
    }

    private static string BuildProjectOverviewMarkdown(ExportExecutionContext context, ExportReport report, TimeSpan duration)
    {
        var fallbackSummary = BuildFallbackSummary(context.Inventory);
        var builder = new StringBuilder();
        builder.AppendLine("# Project Overview");
        builder.AppendLine();
        builder.AppendLine("## Export Summary");
        builder.AppendLine();
        builder.AppendLine($"- Project path: `{context.Options.ProjectPath ?? "Not configured"}`");
        builder.AppendLine($"- Output root: `{context.Options.OutputDirectory}`");
        builder.AppendLine($"- Duration: **{duration:hh\\:mm\\:ss}**");
        builder.AppendLine($"- Results: **{report.Results.Count}** (Succeeded: **{report.SucceededCount}**, Failed: **{report.FailedCount}**, Skipped: **{report.SkippedCount}**)");
        builder.AppendLine($"- Recoverable issues: **{report.Issues.Count}**");
        builder.AppendLine();

        AppendAnalysisHub(builder, context);
        AppendFallbackHotspots(builder, fallbackSummary);

        var failedOrSkipped = report.Results
            .Where(result => result.Status is ExportObjectStatus.Failed or ExportObjectStatus.Skipped)
            .Take(15)
            .ToArray();

        if (failedOrSkipped.Length > 0)
        {
            builder.AppendLine("## Attention Items");
            builder.AppendLine();

            foreach (var result in failedOrSkipped)
            {
                builder.AppendLine($"- {result.ObjectType}/{result.Identifier}: **{result.Status}**{FormatMessage(result.Message)}");
            }

            builder.AppendLine();
        }

        var issueGroups = report.Issues
            .GroupBy(issue => issue.Scope)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(10)
            .ToArray();

        if (issueGroups.Length > 0)
        {
            builder.AppendLine("## Issue Hotspots");
            builder.AppendLine();

            foreach (var issueGroup in issueGroups)
            {
                builder.AppendLine($"- {issueGroup.Key}: **{issueGroup.Count()}**");
            }
        }

        return builder.ToString();
    }

    private static void AppendAnalysisHub(StringBuilder builder, ExportExecutionContext context)
    {
        var analysisArtifacts = context.Artifacts
            .Where(artifact =>
                artifact.RelativePath.Equals("Export/BLOCK_CALL_GRAPH.md", StringComparison.OrdinalIgnoreCase)
                || artifact.RelativePath.Equals("Export/DEPENDENCIES.json", StringComparison.OrdinalIgnoreCase)
                || artifact.RelativePath.StartsWith("Export/Reports/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var inventory = context.Inventory;
        var objectTypeSummary = inventory?.Objects
            .GroupBy(node => node.ObjectType)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        builder.AppendLine("## Analysis Hub");
        builder.AppendLine();
        builder.AppendLine($"- Analysis artifacts generated: **{analysisArtifacts.Length}**");

        if (inventory is not null)
        {
            builder.AppendLine($"- Inventory objects discovered: **{inventory.Objects.Count}**");
            builder.AppendLine($"- Inventory issues: **{inventory.Issues.Count}**");
        }

        builder.AppendLine();

        if (analysisArtifacts.Length > 0)
        {
            builder.AppendLine("### Key Files");
            builder.AppendLine();

            foreach (var artifact in analysisArtifacts)
            {
                builder.AppendLine($"- `{artifact.RelativePath}` ({artifact.Format}, {artifact.ContentLength} bytes)");
            }

            builder.AppendLine();
        }

        if (objectTypeSummary is { Length: > 0 })
        {
            builder.AppendLine("### Top Object Types");
            builder.AppendLine();

            foreach (var entry in objectTypeSummary)
            {
                builder.AppendLine($"- {entry.Key}: **{entry.Count()}**");
            }

            builder.AppendLine();
        }
    }

    private static string BuildExportReportMarkdown(
        ExportExecutionContext context,
        ExportReport report,
        TimeSpan duration,
        DateTimeOffset generatedAt)
    {
        var fallbackSummary = BuildFallbackSummary(context.Inventory);
        var builder = new StringBuilder();
        builder.AppendLine("# Export Report");
        builder.AppendLine();
        builder.AppendLine($"Generated: `{generatedAt:O}`");
        builder.AppendLine();
        builder.AppendLine("## Run Metadata");
        builder.AppendLine();
        builder.AppendLine($"- Project path: `{context.Options.ProjectPath ?? "Not configured"}`");
        builder.AppendLine($"- Start time: `{report.StartedAt:O}`");
        builder.AppendLine($"- End time: `{report.FinishedAt:O}`");
        builder.AppendLine($"- Duration: **{duration:hh\\:mm\\:ss}**");
        builder.AppendLine();
        AppendPackagingSection(builder, context, report);
        builder.AppendLine();
        builder.AppendLine("## Stage/Object Results");
        builder.AppendLine();

        foreach (var result in report.Results)
        {
            builder.AppendLine($"- {result.ObjectType}/{result.Identifier}: **{result.Status}**{FormatMessage(result.Message)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Issues");
        builder.AppendLine();

        if (report.Issues.Count == 0)
        {
            builder.AppendLine("No recoverable issues were recorded.");
        }
        else
        {
            foreach (var issue in report.Issues)
            {
                builder.AppendLine($"- {issue.Scope}: {issue.Message}");

                if (!string.IsNullOrWhiteSpace(issue.Details))
                {
                    builder.AppendLine($"  - Details: `{issue.Details}`");
                }
            }
        }

        builder.AppendLine();
        AppendFallbackHotspots(builder, fallbackSummary);

        return builder.ToString();
    }

    private static FallbackSummary BuildFallbackSummary(TiaProjectInventory? inventory)
    {
        if (inventory is null || inventory.Objects.Count == 0)
        {
            return new FallbackSummary(0, 0, 0, 0, Array.Empty<FallbackHotspotItem>());
        }

        var objects = inventory.Objects;
        var fallbackNodes = objects.Where(node => TryReadBoolMetadata(node.Metadata, "FallbackReflectionUsed")).ToArray();
        var typedNodes = objects.Where(node => TryReadBoolMetadata(node.Metadata, "ExtractedByTypedExtractor")).ToArray();

        var hotspots = fallbackNodes
            .GroupBy(node => new
            {
                Domain = GetMetadata(node.Metadata, "Domain", "Unmapped"),
                RuntimeType = GetMetadata(node.Metadata, "RuntimeType", node.ObjectType),
                node.ObjectType
            })
            .Select(group => new FallbackHotspotItem(
                Domain: group.Key.Domain,
                RuntimeType: group.Key.RuntimeType,
                ObjectType: group.Key.ObjectType,
                ExamplePath: group.First().QualifiedPath,
                Count: group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RuntimeType, StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToArray();

        return new FallbackSummary(
            TotalObjects: objects.Count,
            TotalFallbackObjects: fallbackNodes.Length,
            TotalTypedObjects: typedNodes.Length,
            TotalUniqueFallbackRuntimeTypes: fallbackNodes
                .Select(node => GetMetadata(node.Metadata, "RuntimeType", node.ObjectType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Hotspots: hotspots);
    }

    private static void AppendFallbackHotspots(StringBuilder builder, FallbackSummary summary)
    {
        builder.AppendLine("## Reflection Fallback Hotspots");
        builder.AppendLine();
        builder.AppendLine($"- Total inventory objects: **{summary.TotalObjects}**");
        builder.AppendLine($"- Typed extractor objects: **{summary.TotalTypedObjects}**");
        builder.AppendLine($"- Reflection fallback objects: **{summary.TotalFallbackObjects}**");
        builder.AppendLine($"- Unique fallback runtime types: **{summary.TotalUniqueFallbackRuntimeTypes}**");
        builder.AppendLine();

        if (summary.Hotspots.Count == 0)
        {
            builder.AppendLine("No reflection fallback hotspots were detected in the current inventory.");
            return;
        }

        foreach (var hotspot in summary.Hotspots)
        {
            builder.AppendLine($"- {hotspot.Domain}/{hotspot.ObjectType}: **{hotspot.Count}** (`{hotspot.RuntimeType}`) Example: `{hotspot.ExamplePath}`");
        }
    }

    private static bool TryReadBoolMetadata(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null
        && metadata.TryGetValue(key, out var raw)
        && bool.TryParse(raw, out var value)
        && value;

    private static string GetMetadata(IReadOnlyDictionary<string, string>? metadata, string key, string fallback) =>
        metadata is not null
        && metadata.TryGetValue(key, out var value)
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string FormatMessage(string? message) =>
        string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : $" ({message})";

    private static object BuildArchiveSection(ExportExecutionContext context, ExportReport report)
    {
        var packagingResult = report.Results
            .LastOrDefault(result =>
                result.ObjectType.Equals("Packaging", StringComparison.OrdinalIgnoreCase)
                && result.Identifier.Equals("ExportZip", StringComparison.OrdinalIgnoreCase));

        var expectedPath = Path.Combine(context.Options.OutputDirectory, "Export.zip");

        return new
        {
            context.Options.EnableCompression,
            ExpectedPath = expectedPath,
            Status = packagingResult?.Status.ToString() ?? (context.Options.EnableCompression ? "Pending" : "Skipped"),
            ArchivePath = context.ArchiveInfo?.ArchivePath ?? packagingResult?.Message,
            context.ArchiveInfo?.SizeBytes,
            context.ArchiveInfo?.Sha256,
            context.ArchiveInfo?.GeneratedAt
        };
    }

    private static void AppendPackagingSection(StringBuilder builder, ExportExecutionContext context, ExportReport report)
    {
        var packagingResult = report.Results
            .LastOrDefault(result =>
                result.ObjectType.Equals("Packaging", StringComparison.OrdinalIgnoreCase)
                && result.Identifier.Equals("ExportZip", StringComparison.OrdinalIgnoreCase));

        var expectedPath = Path.Combine(context.Options.OutputDirectory, "Export.zip");

        builder.AppendLine("## Packaging");
        builder.AppendLine();
        builder.AppendLine($"- Compression enabled: **{context.Options.EnableCompression}**");
        builder.AppendLine($"- Expected archive path: `{expectedPath}`");

        if (packagingResult is null)
        {
            builder.AppendLine($"- Archive status: **{(context.Options.EnableCompression ? "Pending" : "Skipped")}**");
            return;
        }

        builder.AppendLine($"- Archive status: **{packagingResult.Status}**");

        if (!string.IsNullOrWhiteSpace(packagingResult.Message))
        {
            builder.AppendLine($"- Archive output: `{packagingResult.Message}`");
        }

        if (context.ArchiveInfo?.SizeBytes is long sizeBytes)
        {
            builder.AppendLine($"- Archive size: **{sizeBytes} bytes**");
        }

        if (!string.IsNullOrWhiteSpace(context.ArchiveInfo?.Sha256))
        {
            builder.AppendLine($"- Archive SHA-256: `{context.ArchiveInfo.Sha256}`");
        }
    }

    private sealed record FallbackSummary(
        int TotalObjects,
        int TotalFallbackObjects,
        int TotalTypedObjects,
        int TotalUniqueFallbackRuntimeTypes,
        IReadOnlyList<FallbackHotspotItem> Hotspots);

    private sealed record FallbackHotspotItem(
        string Domain,
        string RuntimeType,
        string ObjectType,
        string ExamplePath,
        int Count);
}
