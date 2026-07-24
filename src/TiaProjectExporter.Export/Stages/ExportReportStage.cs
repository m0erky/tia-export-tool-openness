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
            await context.ArtifactWriter.WriteArtifactAsync(
                new ExportArtifact("Export/PROJECT_OVERVIEW.md", ExportFormat.Markdown, projectOverview),
                cancellationToken).ConfigureAwait(false);

            await context.ArtifactWriter.WriteArtifactAsync(
                new ExportArtifact("Export/EXPORT_REPORT.md", ExportFormat.Markdown, exportReport),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            await context.ArtifactWriter.WriteArtifactAsync(
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
            ObjectTypes = objectTypeCounts
        };

        return JsonSerializer.Serialize(payload, jsonOptions);
    }

    private static string BuildProjectOverviewMarkdown(ExportExecutionContext context, ExportReport report, TimeSpan duration)
    {
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

    private static string BuildExportReportMarkdown(
        ExportExecutionContext context,
        ExportReport report,
        TimeSpan duration,
        DateTimeOffset generatedAt)
    {
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

        return builder.ToString();
    }

    private static string FormatMessage(string? message) =>
        string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : $" ({message})";
}
