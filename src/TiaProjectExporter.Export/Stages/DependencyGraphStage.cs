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
    private static readonly string[] DependencyMetadataKeys = ["Calls", "DependsOn", "Uses", "References", "Dependencies"];

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

        var edges = inventory.Objects
            .SelectMany(source => ParseDependencies(source.Metadata).Select(target => new
            {
                SourceId = BuildNodeId(source),
                SourceName = source.Name,
                Target = target,
                Relationship = "DependsOn"
            }))
            .Distinct()
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
                TopDependents = topDependents
            }
        };
    }

    private static string BuildNodeId(TiaProjectObjectNode node)
    {
        var seed = string.IsNullOrWhiteSpace(node.QualifiedPath) ? node.Name : node.QualifiedPath;
        return seed.Trim().Replace(' ', '_');
    }

    private static IReadOnlyCollection<string> ParseDependencies(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return Array.Empty<string>();
        }

        return DependencyMetadataKeys
            .Where(metadata.ContainsKey)
            .SelectMany(key => SplitDependencies(metadata[key]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SplitDependencies(string raw)
    {
        char[] separators = [',', ';', '|'];

        return raw
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token));
    }
}
