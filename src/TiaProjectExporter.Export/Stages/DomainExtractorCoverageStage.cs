using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Generates a domain-to-extractor coverage matrix and explicit runtime-type gap list.
/// </summary>
public sealed class DomainExtractorCoverageStage : IExportStage
{
    /// <inheritdoc />
    public string Name => "Domain Extractor Coverage";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var report = BuildReport(inventory);

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var json = JsonSerializer.Serialize(report.JsonPayload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/DOMAIN_EXTRACTOR_COVERAGE.json", ExportFormat.Json, json),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/DOMAIN_EXTRACTOR_COVERAGE.md", ExportFormat.Markdown, BuildMarkdown(report, inventory)),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "DomainExtractorCoverage", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Domain extractor coverage generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static CoverageReport BuildReport(TiaProjectInventory inventory)
    {
        var raw = inventory.Objects
            .Where(node => node.Metadata is not null)
            .Select(node =>
            {
                var metadata = node.Metadata!;
                if (!metadata.TryGetValue("RuntimeType", out var runtimeType) || string.IsNullOrWhiteSpace(runtimeType))
                {
                    return null;
                }

                var domain = ReportDomainCatalog.ResolveDomain(node);

                var typedExtractor = metadata.TryGetValue("TypedExtractor", out var extractor)
                    ? extractor
                    : string.Empty;

                var fallback = metadata.TryGetValue("FallbackReflectionUsed", out var rawFallback)
                    && bool.TryParse(rawFallback, out var fallbackUsed)
                    && fallbackUsed;

                return new RawEntry(
                    Domain: domain,
                    RuntimeType: runtimeType,
                    ObjectType: node.ObjectType,
                    TypedExtractor: typedExtractor,
                    FallbackUsed: fallback);
            })
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToArray();

        var matrix = raw
            .GroupBy(entry => new { entry.Domain, entry.TypedExtractor })
            .Select(group =>
            {
                var runtimeTypeCount = group.Select(item => item.RuntimeType).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var fallbackCount = group.Count(item => item.FallbackUsed);
                var status = string.IsNullOrWhiteSpace(group.Key.TypedExtractor)
                    ? "Gap"
                    : fallbackCount > 0
                        ? "Partial"
                        : "Implemented";

                return new CoverageMatrixEntry(
                    Domain: group.Key.Domain,
                    TypedExtractor: group.Key.TypedExtractor,
                    RuntimeTypeCount: runtimeTypeCount,
                    ObservedCount: group.Count(),
                    FallbackCount: fallbackCount,
                    Status: status);
            })
            .OrderBy(entry => entry.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.TypedExtractor, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var gaps = raw
            .Where(entry => entry.FallbackUsed || string.IsNullOrWhiteSpace(entry.TypedExtractor))
            .GroupBy(entry => new { entry.Domain, entry.RuntimeType, entry.ObjectType })
            .Select(group => new GapEntry(
                Domain: group.Key.Domain,
                RuntimeType: group.Key.RuntimeType,
                ObjectType: group.Key.ObjectType,
                ObservedCount: group.Count(),
                SuggestedExtractor: $"{NormalizeDomain(group.Key.Domain)}DomainExtractor"))
            .OrderByDescending(entry => entry.ObservedCount)
            .ThenBy(entry => entry.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.RuntimeType, StringComparer.OrdinalIgnoreCase)
            .Take(150)
            .ToArray();

        var payload = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            inventory.ProjectName,
            Status = inventory.Status.ToString(),
            Summary = new
            {
                MatrixRows = matrix.Length,
                GapCount = gaps.Length,
                ImplementedRows = matrix.Count(entry => entry.Status == "Implemented"),
                PartialRows = matrix.Count(entry => entry.Status == "Partial"),
                GapRows = matrix.Count(entry => entry.Status == "Gap")
            },
            Matrix = matrix,
            Gaps = gaps
        };

        return new CoverageReport(matrix, gaps, payload);
    }

    private static string BuildMarkdown(CoverageReport report, TiaProjectInventory inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Domain Extractor Coverage");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Inventory status: **{inventory.Status}**");
        builder.AppendLine();
        builder.AppendLine($"Matrix rows: **{report.Matrix.Count}**");
        builder.AppendLine($"Gap entries: **{report.Gaps.Count}**");
        builder.AppendLine();

        builder.AppendLine("## Coverage Matrix");
        builder.AppendLine();
        builder.AppendLine("| Domain | Extractor | Runtime Types | Observed | Fallback | Status |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | --- |");

        foreach (var entry in report.Matrix)
        {
            var extractor = string.IsNullOrWhiteSpace(entry.TypedExtractor) ? "-" : entry.TypedExtractor;
            builder.AppendLine($"| {entry.Domain} | {extractor} | {entry.RuntimeTypeCount} | {entry.ObservedCount} | {entry.FallbackCount} | {entry.Status} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Extractor Gaps");
        builder.AppendLine();

        if (report.Gaps.Count == 0)
        {
            builder.AppendLine("No extractor gaps detected in current inventory.");
            return builder.ToString();
        }

        builder.AppendLine("| Domain | Runtime Type | Object | Observed | Suggested Extractor |");
        builder.AppendLine("| --- | --- | --- | ---: | --- |");

        foreach (var gap in report.Gaps)
        {
            builder.AppendLine($"| {gap.Domain} | {gap.RuntimeType} | {gap.ObjectType} | {gap.ObservedCount} | {gap.SuggestedExtractor} |");
        }

        return builder.ToString();
    }

    private static string NormalizeDomain(string domain)
    {
        var chars = domain.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "Unknown" : new string(chars);
    }

    private sealed record RawEntry(
        string Domain,
        string RuntimeType,
        string ObjectType,
        string TypedExtractor,
        bool FallbackUsed);

    private sealed record CoverageMatrixEntry(
        string Domain,
        string TypedExtractor,
        int RuntimeTypeCount,
        int ObservedCount,
        int FallbackCount,
        string Status);

    private sealed record GapEntry(
        string Domain,
        string RuntimeType,
        string ObjectType,
        int ObservedCount,
        string SuggestedExtractor);

    private sealed record CoverageReport(
        IReadOnlyList<CoverageMatrixEntry> Matrix,
        IReadOnlyList<GapEntry> Gaps,
        object JsonPayload);
}
