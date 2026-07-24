using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Builds a prioritized backlog of runtime types that should receive explicit typed extractor mappings.
/// </summary>
public sealed class TypedExtractorBacklogStage : IExportStage
{
    private static readonly IReadOnlyDictionary<string, string> RelationshipByMetadataKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Calls"] = "Calls",
        ["BlockCalls"] = "Calls",
        ["InvokedBlocks"] = "Calls",
        ["DependsOn"] = "DependsOn",
        ["Dependencies"] = "DependsOn",
        ["Uses"] = "Uses",
        ["UsesType"] = "Uses",
        ["References"] = "References",
        ["ReferencedTags"] = "UsesTag",
        ["TagUsage"] = "UsesTag"
    };

    /// <inheritdoc />
    public string Name => "Typed Extractor Backlog";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var backlog = BuildBacklog(inventory);

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var json = JsonSerializer.Serialize(backlog.JsonPayload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/TYPED_EXTRACTOR_BACKLOG.json", ExportFormat.Json, json),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/TYPED_EXTRACTOR_BACKLOG.md", ExportFormat.Markdown, BuildMarkdown(backlog, inventory)),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "TypedExtractorBacklog", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Typed extractor backlog generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static BacklogReport BuildBacklog(TiaProjectInventory inventory)
    {
        var nodes = inventory.Objects.ToArray();
        var nodeIds = nodes.Select(BuildNodeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nodeNames = nodes.Select(node => node.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unresolvedEdgeCountBySourcePath = nodes
            .ToDictionary(
                node => node.QualifiedPath,
                node => ParseRelationships(node.Metadata)
                    .Count(relation => !IsResolvedTarget(relation.Target, nodeIds, nodeNames)),
                StringComparer.OrdinalIgnoreCase);

        var candidates = nodes
            .Where(node => node.Metadata is not null)
            .Where(node => node.Metadata!.TryGetValue("RuntimeType", out var runtimeType) && !string.IsNullOrWhiteSpace(runtimeType))
            .Select(node =>
            {
                var metadata = node.Metadata!;
                var runtimeType = metadata["RuntimeType"];
                var domain = metadata.TryGetValue("Domain", out var domainValue) && !string.IsNullOrWhiteSpace(domainValue)
                    ? domainValue
                    : "Unknown";
                var typedExtractor = metadata.TryGetValue("TypedExtractor", out var extractor)
                    ? extractor
                    : string.Empty;
                var fallback = metadata.TryGetValue("FallbackReflectionUsed", out var rawFallback)
                    && bool.TryParse(rawFallback, out var fallbackUsed)
                    && fallbackUsed;

                var confidence = metadata.TryGetValue("ExtractionConfidence", out var rawConfidence)
                    && double.TryParse(rawConfidence, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : 0.50;

                unresolvedEdgeCountBySourcePath.TryGetValue(node.QualifiedPath, out var unresolvedEdges);

                return new Candidate(
                    RuntimeType: runtimeType,
                    Domain: domain,
                    ObjectType: node.ObjectType,
                    TypedExtractor: typedExtractor,
                    Fallback: fallback,
                    Confidence: confidence,
                    UnresolvedEdges: unresolvedEdges);
            })
            .Where(candidate => candidate.Fallback || string.IsNullOrWhiteSpace(candidate.TypedExtractor))
            .ToArray();

        var entries = candidates
            .GroupBy(candidate => new { candidate.RuntimeType, candidate.Domain, candidate.ObjectType, candidate.TypedExtractor })
            .Select(group =>
            {
                var frequency = group.Count();
                var fallbackCount = group.Count(item => item.Fallback);
                var avgConfidence = group.Average(item => item.Confidence);
                var unresolvedEdges = group.Sum(item => item.UnresolvedEdges);
                var impact = CalculateImpactScore(frequency, fallbackCount, avgConfidence, unresolvedEdges);

                return new BacklogEntry(
                    RuntimeType: group.Key.RuntimeType,
                    Domain: group.Key.Domain,
                    ObjectType: group.Key.ObjectType,
                    ExistingTypedExtractor: group.Key.TypedExtractor,
                    Frequency: frequency,
                    FallbackCount: fallbackCount,
                    AverageConfidence: Math.Round(avgConfidence, 2),
                    UnresolvedEdges: unresolvedEdges,
                    ImpactScore: impact,
                    SuggestedAction: BuildSuggestedAction(group.Key.Domain, group.Key.RuntimeType, impact));
            })
            .OrderByDescending(entry => entry.ImpactScore)
            .ThenByDescending(entry => entry.Frequency)
            .ThenBy(entry => entry.RuntimeType, StringComparer.OrdinalIgnoreCase)
            .Take(120)
            .ToArray();

        var payload = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            inventory.ProjectName,
            Status = inventory.Status.ToString(),
            Summary = new
            {
                CandidateCount = candidates.Length,
                PrioritizedEntries = entries.Length,
                HighImpactEntries = entries.Count(entry => entry.ImpactScore >= 70),
                DomainsCovered = entries.Select(entry => entry.Domain).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            },
            Entries = entries
        };

        return new BacklogReport(entries, payload);
    }

    private static int CalculateImpactScore(int frequency, int fallbackCount, double averageConfidence, int unresolvedEdges)
    {
        var frequencyScore = Math.Min(45, frequency * 4);
        var fallbackScore = Math.Min(25, fallbackCount * 3);
        var unresolvedScore = Math.Min(20, unresolvedEdges * 2);
        var confidencePenalty = (int)Math.Round((1.0 - averageConfidence) * 20, MidpointRounding.AwayFromZero);

        return Math.Clamp(frequencyScore + fallbackScore + unresolvedScore + confidencePenalty, 0, 100);
    }

    private static string BuildSuggestedAction(string domain, string runtimeType, int impactScore)
    {
        if (impactScore >= 80)
        {
            return $"Immediate: implement dedicated {domain} typed extractor mapping for '{runtimeType}'.";
        }

        if (impactScore >= 60)
        {
            return $"High priority: add explicit {domain} runtime mapping for '{runtimeType}'.";
        }

        return $"Backlog: review '{runtimeType}' and map when adjacent domain extractor work is planned.";
    }

    private static string BuildMarkdown(BacklogReport backlog, TiaProjectInventory inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Typed Extractor Backlog");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Inventory status: **{inventory.Status}**");
        builder.AppendLine();
        builder.AppendLine($"Prioritized entries: **{backlog.Entries.Count}**");
        builder.AppendLine();
        builder.AppendLine("## Top Priorities");
        builder.AppendLine();

        foreach (var entry in backlog.Entries.Take(20))
        {
            builder.AppendLine($"- [{entry.ImpactScore}] {entry.Domain} / {entry.RuntimeType}");
            builder.AppendLine($"  - Frequency: {entry.Frequency}, Fallback: {entry.FallbackCount}, Unresolved edges: {entry.UnresolvedEdges}, Avg confidence: {entry.AverageConfidence:0.00}");
            builder.AppendLine($"  - Action: {entry.SuggestedAction}");
        }

        builder.AppendLine();
        builder.AppendLine("## Full Backlog");
        builder.AppendLine();
        builder.AppendLine("| Impact | Domain | Runtime Type | Object | Frequency | Fallback | Unresolved | Avg Conf. | Existing Extractor |");
        builder.AppendLine("| ---: | --- | --- | --- | ---: | ---: | ---: | ---: | --- |");

        foreach (var entry in backlog.Entries)
        {
            var extractor = string.IsNullOrWhiteSpace(entry.ExistingTypedExtractor) ? "-" : entry.ExistingTypedExtractor;
            builder.AppendLine($"| {entry.ImpactScore} | {entry.Domain} | {entry.RuntimeType} | {entry.ObjectType} | {entry.Frequency} | {entry.FallbackCount} | {entry.UnresolvedEdges} | {entry.AverageConfidence:0.00} | {extractor} |");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<Relationship> ParseRelationships(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return Array.Empty<Relationship>();
        }

        return RelationshipByMetadataKey
            .Where(entry => metadata.ContainsKey(entry.Key))
            .SelectMany(entry => SplitValues(metadata[entry.Key]).Select(target => new Relationship(target, entry.Value)))
            .DistinctBy(entry => $"{entry.Target}|{entry.Kind}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SplitValues(string raw)
    {
        char[] separators = [',', ';', '|'];
        return raw.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsResolvedTarget(string target, IReadOnlySet<string> nodeIds, IReadOnlySet<string> nodeNames)
    {
        if (nodeIds.Contains(target) || nodeNames.Contains(target))
        {
            return true;
        }

        return nodeIds.Any(nodeId => nodeId.EndsWith($"/{target}", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildNodeId(TiaProjectObjectNode node)
    {
        var seed = string.IsNullOrWhiteSpace(node.QualifiedPath) ? node.Name : node.QualifiedPath;
        return seed.Trim().Replace(' ', '_');
    }

    private sealed record Relationship(string Target, string Kind);

    private sealed record Candidate(
        string RuntimeType,
        string Domain,
        string ObjectType,
        string TypedExtractor,
        bool Fallback,
        double Confidence,
        int UnresolvedEdges);

    private sealed record BacklogEntry(
        string RuntimeType,
        string Domain,
        string ObjectType,
        string ExistingTypedExtractor,
        int Frequency,
        int FallbackCount,
        double AverageConfidence,
        int UnresolvedEdges,
        int ImpactScore,
        string SuggestedAction);

    private sealed record BacklogReport(IReadOnlyList<BacklogEntry> Entries, object JsonPayload);
}
