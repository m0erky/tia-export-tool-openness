using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Produces a runtime type catalog with version context and extractor mapping suggestions.
/// </summary>
public sealed class RuntimeTypeCatalogStage : IExportStage
{
    /// <inheritdoc />
    public string Name => "Runtime Type Catalog";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var catalog = BuildCatalog(inventory);

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var json = JsonSerializer.Serialize(catalog.JsonPayload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/RUNTIME_TYPE_CATALOG.json", ExportFormat.Json, json),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/RUNTIME_TYPE_CATALOG.md", ExportFormat.Markdown, BuildMarkdown(catalog, inventory)),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "RuntimeTypeCatalog", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Runtime type catalog generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static RuntimeTypeCatalog BuildCatalog(TiaProjectInventory inventory)
    {
        var tiaVersion = inventory.Objects
            .Where(node => node.ObjectType.Equals("OpennessRuntime", StringComparison.OrdinalIgnoreCase))
            .Select(node => node.Metadata is not null && node.Metadata.TryGetValue("Version", out var version) ? version : null)
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
            ?? "Unknown";

        var entries = inventory.Objects
            .SelectMany(node => BuildEntry(node, tiaVersion))
            .GroupBy(entry => new { entry.TiaVersion, entry.RuntimeType, entry.Domain, entry.ObjectType, entry.TypedExtractor, entry.FallbackUsed, entry.Suggestion })
            .Select(group => new CatalogEntry(
                group.Key.TiaVersion,
                group.Key.RuntimeType,
                group.Key.Domain,
                group.Key.ObjectType,
                group.Key.TypedExtractor,
                group.Key.FallbackUsed,
                group.Key.Suggestion,
                group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.RuntimeType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var topSuggestions = entries
            .Where(entry => entry.FallbackUsed || string.IsNullOrWhiteSpace(entry.TypedExtractor))
            .GroupBy(entry => entry.Suggestion)
            .OrderByDescending(group => group.Sum(entry => entry.Count))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(group => new SuggestionSummary(group.Key, group.Sum(entry => entry.Count)))
            .ToArray();

        var payload = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            inventory.ProjectName,
            Status = inventory.Status.ToString(),
            TiaVersion = tiaVersion,
            Summary = new
            {
                RuntimeTypeCount = entries.Select(entry => entry.RuntimeType).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                CatalogEntries = entries.Length,
                FallbackEntries = entries.Count(entry => entry.FallbackUsed),
                TypedEntries = entries.Count(entry => !string.IsNullOrWhiteSpace(entry.TypedExtractor))
            },
            Suggestions = topSuggestions,
            Entries = entries
        };

        return new RuntimeTypeCatalog(tiaVersion, entries, topSuggestions, payload);
    }

    private static IEnumerable<CandidateEntry> BuildEntry(TiaProjectObjectNode node, string tiaVersion)
    {
        if (node.Metadata is null)
        {
            yield break;
        }

        if (!node.Metadata.TryGetValue("RuntimeType", out var runtimeType) || string.IsNullOrWhiteSpace(runtimeType))
        {
            yield break;
        }

        var typedExtractor = node.Metadata.TryGetValue("TypedExtractor", out var extractor)
            ? extractor
            : string.Empty;

        var fallbackUsed = node.Metadata.TryGetValue("FallbackReflectionUsed", out var rawFallback)
            && bool.TryParse(rawFallback, out var fallback)
            && fallback;

        var domain = node.Metadata.TryGetValue("Domain", out var metadataDomain)
            && !string.IsNullOrWhiteSpace(metadataDomain)
                ? metadataDomain
                : "Unknown";

        yield return new CandidateEntry(
            TiaVersion: tiaVersion,
            RuntimeType: runtimeType,
            Domain: domain,
            ObjectType: node.ObjectType,
            TypedExtractor: typedExtractor,
            FallbackUsed: fallbackUsed,
            Suggestion: BuildSuggestion(domain, node.ObjectType, runtimeType, typedExtractor, fallbackUsed));
    }

    private static string BuildSuggestion(string domain, string objectType, string runtimeType, string typedExtractor, bool fallbackUsed)
    {
        if (!fallbackUsed && !string.IsNullOrWhiteSpace(typedExtractor))
        {
            return $"Keep current mapping ({typedExtractor}).";
        }

        if (fallbackUsed)
        {
            return $"Add typed extractor mapping for {domain} runtime type '{runtimeType}' (current object '{objectType}').";
        }

        return $"Review runtime type '{runtimeType}' for explicit {domain} extractor mapping.";
    }

    private static string BuildMarkdown(RuntimeTypeCatalog catalog, TiaProjectInventory inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Runtime Type Catalog");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Inventory status: **{inventory.Status}**");
        builder.AppendLine($"Detected TIA version context: **{catalog.TiaVersion}**");
        builder.AppendLine();
        builder.AppendLine($"Catalog entries: **{catalog.Entries.Count}**");
        builder.AppendLine($"Unique runtime types: **{catalog.Entries.Select(entry => entry.RuntimeType).Distinct(StringComparer.OrdinalIgnoreCase).Count()}**");
        builder.AppendLine();

        builder.AppendLine("## Top Suggestions");
        builder.AppendLine();

        if (catalog.Suggestions.Count == 0)
        {
            builder.AppendLine("No additional mapping suggestions detected.");
        }
        else
        {
            foreach (var suggestion in catalog.Suggestions)
            {
                builder.AppendLine($"- {suggestion.Suggestion}: **{suggestion.Count}**");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Runtime Types");
        builder.AppendLine();
        builder.AppendLine("| Version | Runtime Type | Domain | Object | Typed Extractor | Fallback | Count |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | ---: |");

        foreach (var entry in catalog.Entries.Take(300))
        {
            builder.AppendLine($"| {entry.TiaVersion} | {entry.RuntimeType} | {entry.Domain} | {entry.ObjectType} | {(string.IsNullOrWhiteSpace(entry.TypedExtractor) ? "-" : entry.TypedExtractor)} | {entry.FallbackUsed} | {entry.Count} |");
        }

        return builder.ToString();
    }

    private sealed record CandidateEntry(
        string TiaVersion,
        string RuntimeType,
        string Domain,
        string ObjectType,
        string TypedExtractor,
        bool FallbackUsed,
        string Suggestion);

    private sealed record CatalogEntry(
        string TiaVersion,
        string RuntimeType,
        string Domain,
        string ObjectType,
        string TypedExtractor,
        bool FallbackUsed,
        string Suggestion,
        int Count);

    private sealed record SuggestionSummary(string Suggestion, int Count);

    private sealed record RuntimeTypeCatalog(
        string TiaVersion,
        IReadOnlyList<CatalogEntry> Entries,
        IReadOnlyList<SuggestionSummary> Suggestions,
        object JsonPayload);
}
