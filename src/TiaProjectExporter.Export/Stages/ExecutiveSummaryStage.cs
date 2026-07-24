using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Generates a concise executive summary that consolidates readiness, risks, and next actions.
/// </summary>
public sealed class ExecutiveSummaryStage : IExportStage
{
    /// <inheritdoc />
    public string Name => "Executive Summary";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var report = context.BuildReport();
        var inventory = context.Inventory;
        var summary = BuildSummary(context, report, inventory);

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var json = JsonSerializer.Serialize(summary.JsonPayload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/EXECUTIVE_SUMMARY.json", ExportFormat.Json, json),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/EXECUTIVE_SUMMARY.md", ExportFormat.Markdown, BuildMarkdown(summary, context, report, inventory)),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "ExecutiveSummary", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Executive summary generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static SummaryModel BuildSummary(ExportExecutionContext context, ExportReport report, TiaProjectInventory? inventory)
    {
        var analysisArtifacts = context.Artifacts.Count(artifact => artifact.RelativePath.StartsWith("Export/Reports/", StringComparison.OrdinalIgnoreCase));
        var inventoryObjects = inventory?.Objects ?? Array.Empty<TiaProjectObjectNode>();
        var fallbackObjects = inventoryObjects.Count(node =>
            node.Metadata is not null
            && node.Metadata.TryGetValue("FallbackReflectionUsed", out var raw)
            && bool.TryParse(raw, out var fallback)
            && fallback);

        var unresolvedHints = inventoryObjects.Count(node =>
            node.Metadata is not null
            && (node.Metadata.ContainsKey("References") || node.Metadata.ContainsKey("Dependencies") || node.Metadata.ContainsKey("Calls"))
            && node.Metadata.TryGetValue("FallbackReflectionUsed", out var raw)
            && bool.TryParse(raw, out var fallback)
            && fallback);

        var topDomains = inventoryObjects
            .Select(node => node.Metadata is not null && node.Metadata.TryGetValue("Domain", out var domain) ? domain : "Unknown")
            .GroupBy(domain => domain)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(group => new DomainCount(group.Key, group.Count()))
            .ToArray();

        var priorities = new List<string>();

        if (fallbackObjects > 0)
        {
            priorities.Add($"Reduce fallback extraction: {fallbackObjects} objects still use reflection fallback.");
        }

        if (report.FailedCount > 0)
        {
            priorities.Add($"Investigate failed results: {report.FailedCount} failed stage/object results.");
        }

        if (report.Issues.Count > 0)
        {
            priorities.Add($"Address recoverable issues: {report.Issues.Count} issue entries reported.");
        }

        if (unresolvedHints > 0)
        {
            priorities.Add($"Improve relationship resolution for fallback-heavy nodes ({unresolvedHints} candidates).");
        }

        if (priorities.Count == 0)
        {
            priorities.Add("No critical issues detected; continue expanding typed extractor coverage.");
        }

        var payload = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            report.StartedAt,
            report.FinishedAt,
            DurationSeconds = Math.Round((report.FinishedAt - report.StartedAt).TotalSeconds, 2),
            Totals = new
            {
                report.Results.Count,
                report.SucceededCount,
                report.FailedCount,
                report.SkippedCount,
                report.Issues.Count
            },
            Inventory = new
            {
                Status = inventory?.Status.ToString() ?? "Unavailable",
                ObjectCount = inventoryObjects.Count,
                FallbackObjects = fallbackObjects,
                TopDomains = topDomains
            },
            AnalysisArtifacts = analysisArtifacts,
            Priorities = priorities
        };

        return new SummaryModel(topDomains, priorities, payload);
    }

    private static string BuildMarkdown(
        SummaryModel summary,
        ExportExecutionContext context,
        ExportReport report,
        TiaProjectInventory? inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Executive Summary");
        builder.AppendLine();
        builder.AppendLine($"Project path: `{context.Options.ProjectPath ?? "Not configured"}`");
        builder.AppendLine($"Output root: `{context.Options.OutputDirectory}`");
        builder.AppendLine();
        builder.AppendLine("## Run Health");
        builder.AppendLine();
        builder.AppendLine($"- Duration: **{(report.FinishedAt - report.StartedAt):hh\\:mm\\:ss}**");
        builder.AppendLine($"- Results: **{report.Results.Count}** (Succeeded: **{report.SucceededCount}**, Failed: **{report.FailedCount}**, Skipped: **{report.SkippedCount}**) ");
        builder.AppendLine($"- Issues: **{report.Issues.Count}**");
        builder.AppendLine($"- Inventory status: **{inventory?.Status.ToString() ?? "Unavailable"}**");
        builder.AppendLine($"- Inventory objects: **{inventory?.Objects.Count ?? 0}**");
        builder.AppendLine();

        builder.AppendLine("## Top Domains");
        builder.AppendLine();

        if (summary.TopDomains.Count == 0)
        {
            builder.AppendLine("No domain distribution available.");
        }
        else
        {
            foreach (var domain in summary.TopDomains)
            {
                builder.AppendLine($"- {domain.Domain}: **{domain.Count}**");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Priority Actions");
        builder.AppendLine();

        foreach (var item in summary.Priorities)
        {
            builder.AppendLine($"- {item}");
        }

        builder.AppendLine();
        builder.AppendLine("## Key Artifacts");
        builder.AppendLine();

        foreach (var artifact in context.Artifacts
                     .Where(artifact => artifact.RelativePath.StartsWith("Export/Reports/", StringComparison.OrdinalIgnoreCase)
                         || artifact.RelativePath.Equals("Export/EXECUTIVE_SUMMARY.md", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase)
                     .Take(25))
        {
            builder.AppendLine($"- `{artifact.RelativePath}` ({artifact.ContentLength} bytes)");
        }

        return builder.ToString();
    }

    private sealed record DomainCount(string Domain, int Count);

    private sealed record SummaryModel(
        IReadOnlyList<DomainCount> TopDomains,
        IReadOnlyList<string> Priorities,
        object JsonPayload);
}
