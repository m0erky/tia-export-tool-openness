using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Generates dependency graph JSON from inventory object metadata.
/// </summary>
public sealed class DependencyGraphStage : IExportStage
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
    public string Name => "Dependency Graph";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Options.Formats.Contains(ExportFormat.Json))
        {
            return;
        }

        var inventory = context.Inventory;
        var graph = BuildGraph(inventory);
        var json = JsonSerializer.Serialize(graph, JsonOptionsFactory.CreateDefault());

        await context.WriteArtifactAsync(
            new ExportArtifact("Export/DEPENDENCIES.json", ExportFormat.Json, json),
            cancellationToken).ConfigureAwait(false);

        context.AddResult(new ExportedObjectResult("Analysis", "Dependencies", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Dependencies generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static object BuildGraph(TiaProjectInventory? inventory)
    {
        var generatedAt = DateTimeOffset.UtcNow;

        if (inventory is null)
        {
            return new
            {
                GeneratedAt = generatedAt,
                Status = "NoInventory",
                Nodes = Array.Empty<object>(),
                Edges = Array.Empty<object>(),
                Summary = new
                {
                    NodeCount = 0,
                    EdgeCount = 0
                }
            };
        }

        var nodes = inventory.Objects
            .Select(node => new
            {
                Id = BuildNodeId(node),
                node.Name,
                node.ObjectType,
                node.QualifiedPath,
                node.Depth
            })
            .DistinctBy(node => node.Id)
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var nodeIds = nodes
            .Select(node => node.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nodeNames = nodes
            .Select(node => node.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var instanceTargetMap = CallRelationshipExtractor.BuildInstanceTargetMap(inventory);

        var edges = inventory.Objects
            .SelectMany(source => ParseDependencies(source.Metadata, instanceTargetMap).Select(relation => new
            {
                SourceId = BuildNodeId(source),
                SourceName = source.Name,
                Target = relation.Target,
                Relationship = relation.Relationship,
                relation.MetadataKey,
                Resolved = RelationshipTargetResolver.IsResolvedTarget(relation.Target, nodeIds, nodeNames)
            }))
            .DistinctBy(edge => $"{edge.SourceId}|{edge.Target}|{edge.Relationship}")
            .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var topDependents = edges
            .GroupBy(edge => edge.SourceName)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .ToArray();

        var relationships = edges
            .GroupBy(edge => edge.Relationship)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Relationship = group.Key, Count = group.Count() })
            .ToArray();

        var unresolved = edges
            .Where(edge => !edge.Resolved)
            .GroupBy(edge => edge.Target)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .Select(group => new { Target = group.Key, Count = group.Count() })
            .ToArray();

        return new
        {
            GeneratedAt = generatedAt,
            Status = inventory.Status.ToString(),
            ProjectName = inventory.ProjectName,
            Nodes = nodes,
            Edges = edges,
            Summary = new
            {
                NodeCount = nodes.Length,
                EdgeCount = edges.Length,
                ResolvedEdges = edges.Count(edge => edge.Resolved),
                UnresolvedEdges = edges.Count(edge => !edge.Resolved),
                TopDependents = topDependents,
                Relationships = relationships,
                TopUnresolvedTargets = unresolved
            }
        };
    }

    private static string BuildNodeId(TiaProjectObjectNode node)
    {
        var seed = string.IsNullOrWhiteSpace(node.QualifiedPath) ? node.Name : node.QualifiedPath;
        return seed.Trim().Replace(' ', '_');
    }

    private static IReadOnlyCollection<DependencyRelation> ParseDependencies(
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
            .SelectMany(entry => SplitDependencies(metadata[entry.Key]).Select(target => new DependencyRelation(target, entry.Value, entry.Key)))
            .ToList();

        relations.AddRange(
            CallRelationshipExtractor.ExtractCallRelations(metadata, instanceTargetMap)
                .Select(relation => new DependencyRelation(relation.Target, "Calls", relation.MetadataKey)));

        return relations
            .DistinctBy(relation => $"{relation.Target}|{relation.Relationship}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SplitDependencies(string raw)
    {
        return raw
            .Split(DependencySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RelationshipTargetResolver.NormalizeTarget)
            .Where(token => !string.IsNullOrWhiteSpace(token));
    }

    private sealed record DependencyRelation(string Target, string Relationship, string MetadataKey);
}
