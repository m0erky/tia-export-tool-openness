using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Produces a domain-level export coverage matrix from current inventory results.
/// </summary>
public sealed class ExportCoverageMatrixStage : IExportStage
{
    /// <inheritdoc />
    public string Name => "Coverage Matrix";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var entries = BuildEntries(inventory).ToArray();

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var payload = new
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                inventory.ProjectName,
                InventoryStatus = inventory.Status.ToString(),
                Domains = entries,
                Summary = new
                {
                    CompleteCandidate = entries.Count(entry => entry.Status == "CompleteCandidate"),
                    PartialCandidate = entries.Count(entry => entry.Status == "PartialCandidate"),
                    LowConfidence = entries.Count(entry => entry.Status == "LowConfidence"),
                    NotDiscovered = entries.Count(entry => entry.Status == "NotDiscovered"),
                    TypedExtractorDomains = entries.Count(entry => entry.ExtractedByTypedExtractor),
                    FallbackDomains = entries.Count(entry => entry.FallbackReflectionUsed)
                }
            };

            var json = JsonSerializer.Serialize(payload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/EXPORT_COVERAGE_MATRIX.json", ExportFormat.Json, json),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            var markdown = BuildMarkdown(inventory, entries);
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/EXPORT_COVERAGE_MATRIX.md", ExportFormat.Markdown, markdown),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "CoverageMatrix", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Coverage matrix generated", entries.Length, entries.Length, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static IEnumerable<CoverageEntry> BuildEntries(TiaProjectInventory inventory)
    {
        foreach (var domain in ReportDomainCatalog.DomainOrder)
        {
            var domainNodes = inventory.Objects.Where(node => ReportDomainCatalog.DomainMatches(node, domain)).ToArray();
            var discovered = domainNodes.Length;

            var confident = domainNodes.Count(node =>
                node.Metadata is not null
                && node.Metadata.TryGetValue("ExtractionConfidence", out var raw)
                && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var score)
                && score >= 0.70);

            var issues = ReportDomainCatalog.CountIssuesForDomain(inventory, domain);

            var typedCount = domainNodes.Count(node =>
                node.Metadata is not null
                && node.Metadata.TryGetValue("ExtractedByTypedExtractor", out var raw)
                && bool.TryParse(raw, out var extractedByTyped)
                && extractedByTyped);

            var fallbackCount = domainNodes.Count(node =>
                node.Metadata is not null
                && node.Metadata.TryGetValue("FallbackReflectionUsed", out var raw)
                && bool.TryParse(raw, out var fallback)
                && fallback);

            var status = discovered == 0
                ? "NotDiscovered"
                : confident == discovered
                    ? "CompleteCandidate"
                    : confident > 0
                        ? "PartialCandidate"
                        : "LowConfidence";

            var supportedByApi = ReportDomainCatalog.SupportedByApiMap.TryGetValue(domain, out var supported) && supported;

            yield return new CoverageEntry(
                Domain: domain,
                SupportedByApi: supportedByApi,
                Discovered: discovered,
                HighConfidence: confident,
                TypedCount: typedCount,
                FallbackCount: fallbackCount,
                Issues: issues,
                Status: status,
                ExtractedByTypedExtractor: typedCount > 0,
                FallbackReflectionUsed: fallbackCount > 0);
        }
    }

    private static string BuildMarkdown(TiaProjectInventory inventory, IReadOnlyCollection<CoverageEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Export Coverage Matrix");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Inventory status: **{inventory.Status}**");
        builder.AppendLine();
        builder.AppendLine("| Domain | API | Discovered | High Conf. | Typed | Fallback | Issues | Status |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |");

        foreach (var entry in entries)
        {
            builder.AppendLine($"| {entry.Domain} | {(entry.SupportedByApi ? "Yes" : "No")} | {entry.Discovered} | {entry.HighConfidence} | {entry.TypedCount} | {entry.FallbackCount} | {entry.Issues} | {entry.Status} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Legend");
        builder.AppendLine();
        builder.AppendLine("- **CompleteCandidate**: discovered objects with all entries at high extraction confidence.");
        builder.AppendLine("- **PartialCandidate**: discovered objects with mixed confidence.");
        builder.AppendLine("- **LowConfidence**: discovered objects with no high-confidence entries.");
        builder.AppendLine("- **NotDiscovered**: no objects discovered for the domain in current export.");
        builder.AppendLine("- **Typed**: objects produced by explicit typed extractor mappings.");
        builder.AppendLine("- **Fallback**: objects produced by generic reflection fallback extraction.");

        return builder.ToString();
    }

    private sealed record CoverageEntry(
        string Domain,
        bool SupportedByApi,
        int Discovered,
        int HighConfidence,
        int TypedCount,
        int FallbackCount,
        int Issues,
        string Status,
        bool ExtractedByTypedExtractor,
        bool FallbackReflectionUsed);
}
