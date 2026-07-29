using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Generates AI-oriented relationship insights from inventory dependency metadata.
/// </summary>
public sealed class RelationshipInsightsStage : IExportStage
{
    private static readonly char[] DependencySeparators = [',', ';', '|'];
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
    public string Name => "Relationship Insights";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var insights = Analyze(inventory);

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var json = JsonSerializer.Serialize(insights.JsonPayload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/RELATIONSHIP_INSIGHTS.json", ExportFormat.Json, json),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/RELATIONSHIP_INSIGHTS.md", ExportFormat.Markdown, BuildMarkdown(insights, inventory)),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "RelationshipInsights", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Relationship insights generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static RelationshipInsights Analyze(TiaProjectInventory inventory)
    {
        var nodes = inventory.Objects
            .Select(node => new GraphNode(BuildNodeId(node), node.Name, node.ObjectType, node.QualifiedPath))
            .DistinctBy(node => node.Id)
            .ToArray();

        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nodeNames = nodes.Select(node => node.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var instanceTargetMap = CallRelationshipExtractor.BuildInstanceTargetMap(inventory);

        var edges = inventory.Objects
            .SelectMany(source => ParseRelationships(source.Metadata, instanceTargetMap).Select(relation => new GraphEdge(
                SourceId: BuildNodeId(source),
                SourceName: source.Name,
                Target: relation.Target,
                Relationship: relation.Relationship,
                MetadataKey: relation.MetadataKey,
                Resolved: RelationshipTargetResolver.IsResolvedTarget(relation.Target, nodeIds, nodeNames))))
            .DistinctBy(edge => $"{edge.SourceId}|{edge.Target}|{edge.Relationship}", StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var topCallers = edges
            .Where(edge => edge.Relationship == "Calls")
            .GroupBy(edge => edge.SourceName)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(group => new InsightCount(group.Key, group.Count()))
            .ToArray();

        var topTagConsumers = edges
            .Where(edge => edge.Relationship == "UsesTag")
            .GroupBy(edge => edge.SourceName)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(group => new InsightCount(group.Key, group.Count()))
            .ToArray();

        var topDependencies = edges
            .GroupBy(edge => edge.Target)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(group => new InsightCount(group.Key, group.Count()))
            .ToArray();

        var unresolvedHotspots = edges
            .Where(edge => !edge.Resolved)
            .GroupBy(edge => edge.Target)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(group => new UnresolvedHotspot(
                Target: group.Key,
                Count: group.Count(),
                Relationships: group
                    .GroupBy(edge => edge.Relationship)
                    .OrderByDescending(relation => relation.Count())
                    .ThenBy(relation => relation.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(relation => new InsightCount(relation.Key, relation.Count()))
                    .ToArray()))
            .ToArray();

        var relationshipBreakdown = edges
            .GroupBy(edge => edge.Relationship)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new InsightCount(group.Key, group.Count()))
            .ToArray();

        var payload = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            inventory.ProjectName,
            Status = inventory.Status.ToString(),
            Summary = new
            {
                NodeCount = nodes.Length,
                EdgeCount = edges.Length,
                ResolvedEdges = edges.Count(edge => edge.Resolved),
                UnresolvedEdges = edges.Count(edge => !edge.Resolved)
            },
            Relationships = relationshipBreakdown,
            TopCallers = topCallers,
            TopTagConsumers = topTagConsumers,
            TopDependencies = topDependencies,
            UnresolvedHotspots = unresolvedHotspots
        };

        return new RelationshipInsights(
            nodes,
            edges,
            relationshipBreakdown,
            topCallers,
            topTagConsumers,
            topDependencies,
            unresolvedHotspots,
            payload);
    }

    private static string BuildMarkdown(RelationshipInsights insights, TiaProjectInventory inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Relationship Insights");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Inventory status: **{inventory.Status}**");
        builder.AppendLine();
        builder.AppendLine($"- Nodes: **{insights.Nodes.Count}**");
        builder.AppendLine($"- Edges: **{insights.Edges.Count}**");
        builder.AppendLine($"- Resolved edges: **{insights.Edges.Count(edge => edge.Resolved)}**");
        builder.AppendLine($"- Unresolved edges: **{insights.Edges.Count(edge => !edge.Resolved)}**");
        builder.AppendLine();

        AppendSection(builder, "Relationship Breakdown", insights.RelationshipBreakdown);
        AppendSection(builder, "Top Callers", insights.TopCallers);
        AppendSection(builder, "Top Tag Consumers", insights.TopTagConsumers);
        AppendSection(builder, "Most Referenced Targets", insights.TopDependencies);

        builder.AppendLine("## Unresolved Hotspots");
        builder.AppendLine();

        if (insights.UnresolvedHotspots.Count == 0)
        {
            builder.AppendLine("No unresolved hotspots detected in current metadata.");
            return builder.ToString();
        }

        foreach (var hotspot in insights.UnresolvedHotspots)
        {
            var relationshipText = string.Join(", ", hotspot.Relationships.Select(entry => $"{entry.Name}:{entry.Count}"));
            builder.AppendLine($"- {hotspot.Target}: **{hotspot.Count}** ({relationshipText})");
        }

        builder.AppendLine();
        builder.AppendLine("## Guidance");
        builder.AppendLine();
        builder.AppendLine("- Prioritize typed extractor extensions for unresolved targets with highest counts.");
        builder.AppendLine("- Validate unresolved `Calls` with Siemens block-reference APIs.");
        builder.AppendLine("- Validate unresolved `UsesTag` with Siemens tag-link/reference APIs.");

        return builder.ToString();
    }

    private static void AppendSection(StringBuilder builder, string title, IReadOnlyList<InsightCount> entries)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("No entries discovered.");
            builder.AppendLine();
            return;
        }

        foreach (var entry in entries)
        {
            builder.AppendLine($"- {entry.Name}: **{entry.Count}**");
        }

        builder.AppendLine();
    }

    private static IReadOnlyList<DependencyRelation> ParseRelationships(
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyDictionary<string, string> instanceTargetMap)
    {
        if (metadata is null)
        {
            return CallRelationshipExtractor.ExtractCallRelations(metadata, instanceTargetMap)
                .Select(relation => new DependencyRelation(relation.Target, "Calls", relation.MetadataKey))
                .ToArray();
        }

        var relations = RelationshipByMetadataKey
            .Where(entry => metadata.ContainsKey(entry.Key))
            .SelectMany(entry => SplitValues(metadata[entry.Key]).Select(target => new DependencyRelation(target, entry.Value, entry.Key)))
            .ToList();

        relations.AddRange(
            CallRelationshipExtractor.ExtractCallRelations(metadata, instanceTargetMap)
                .Select(relation => new DependencyRelation(relation.Target, "Calls", relation.MetadataKey)));

        return relations
            .DistinctBy(relation => $"{relation.Target}|{relation.Relationship}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SplitValues(string raw)
    {
        return raw
            .Split(DependencySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RelationshipTargetResolver.NormalizeTarget)
            .Where(token => !string.IsNullOrWhiteSpace(token));
    }

    private static string BuildNodeId(TiaProjectObjectNode node)
    {
        var seed = string.IsNullOrWhiteSpace(node.QualifiedPath) ? node.Name : node.QualifiedPath;
        return seed.Trim().Replace(' ', '_');
    }

    private sealed record DependencyRelation(string Target, string Relationship, string MetadataKey);

    private sealed record GraphNode(string Id, string Name, string ObjectType, string QualifiedPath);

    private sealed record GraphEdge(string SourceId, string SourceName, string Target, string Relationship, string MetadataKey, bool Resolved);

    private sealed record InsightCount(string Name, int Count);

    private sealed record UnresolvedHotspot(string Target, int Count, IReadOnlyList<InsightCount> Relationships);

    private sealed record RelationshipInsights(
        IReadOnlyList<GraphNode> Nodes,
        IReadOnlyList<GraphEdge> Edges,
        IReadOnlyList<InsightCount> RelationshipBreakdown,
        IReadOnlyList<InsightCount> TopCallers,
        IReadOnlyList<InsightCount> TopTagConsumers,
        IReadOnlyList<InsightCount> TopDependencies,
        IReadOnlyList<UnresolvedHotspot> UnresolvedHotspots,
        object JsonPayload);
}
