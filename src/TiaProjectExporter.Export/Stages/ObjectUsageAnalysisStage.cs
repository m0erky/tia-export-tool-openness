using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Generates tag usage and unused object analysis artifacts.
/// </summary>
public sealed class ObjectUsageAnalysisStage : IExportStage
{
    private static readonly string[] DependencyKeys = ["Calls", "DependsOn", "Uses", "References", "Dependencies", "TagUsage", "ReferencedTags"];
    private static readonly char[] DependencySeparators = [',', ';', '|'];

    /// <inheritdoc />
    public string Name => "Usage Analysis";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var usage = Analyze(inventory);
        var jsonOptions = JsonOptionsFactory.CreateDefault();

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/TAG_USAGE.json", ExportFormat.Json, JsonSerializer.Serialize(usage.TagUsagePayload, jsonOptions)),
                cancellationToken).ConfigureAwait(false);

            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/UNUSED_OBJECTS.json", ExportFormat.Json, JsonSerializer.Serialize(usage.UnusedPayload, jsonOptions)),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/TAG_USAGE.md", ExportFormat.Markdown, BuildTagUsageMarkdown(usage)),
                cancellationToken).ConfigureAwait(false);

            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/UNUSED_OBJECTS.md", ExportFormat.Markdown, BuildUnusedMarkdown(usage)),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "Usage", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Tag and unused-object analysis generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static AnalysisResult Analyze(TiaProjectInventory inventory)
    {
        var objects = inventory.Objects
            .Select(node => new Node(node, BuildNodeId(node)))
            .GroupBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => SelectPreferredNode(group))
            .ToArray();

        var byId = objects.ToDictionary(item => item.Id, item => item, StringComparer.OrdinalIgnoreCase);
        var byName = objects
            .GroupBy(item => item.Source.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var inboundById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in objects)
        {
            foreach (var targetToken in ParseDependencies(source.Source.Metadata))
            {
                var target = ResolveTarget(targetToken, byId, byName);
                if (target is null)
                {
                    continue;
                }

                inboundById[target.Id] = inboundById.TryGetValue(target.Id, out var count) ? count + 1 : 1;
            }
        }

        var tagNodes = objects.Where(node => IsTagNode(node.Source)).ToArray();
        var tagUsage = tagNodes
            .Select(tag => new TagUsageItem(
                tag.Source.Name,
                tag.Source.QualifiedPath,
                inboundById.TryGetValue(tag.Id, out var count) ? count : 0))
            .OrderByDescending(item => item.UsageCount)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var unusedObjects = objects
            .Where(node => IsUnusedCandidate(node.Source))
            .Where(node => !IsEntryPoint(node.Source))
            .Where(node => !inboundById.ContainsKey(node.Id))
            .Select(node => new UnusedItem(
                node.Source.Name,
                node.Source.ObjectType,
                node.Source.QualifiedPath))
            .OrderBy(item => item.ObjectType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tagUsagePayload = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            inventory.ProjectName,
            Status = inventory.Status.ToString(),
            TotalTags = tagNodes.Length,
            UsedTags = tagUsage.Count(item => item.UsageCount > 0),
            UnusedTags = tagUsage.Count(item => item.UsageCount == 0),
            Tags = tagUsage
        };

        var unusedPayload = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            inventory.ProjectName,
            Status = inventory.Status.ToString(),
            CandidateCount = objects.Count(node => IsUnusedCandidate(node.Source)),
            UnusedCount = unusedObjects.Length,
            Objects = unusedObjects
        };

        return new AnalysisResult(tagUsage, unusedObjects, tagUsagePayload, unusedPayload);
    }

    private static string BuildTagUsageMarkdown(AnalysisResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Tag Usage Analysis");
        builder.AppendLine();
        builder.AppendLine($"Total tags: **{result.TagUsage.Count}**");
        builder.AppendLine($"Used tags: **{result.TagUsage.Count(item => item.UsageCount > 0)}**");
        builder.AppendLine($"Unused tags: **{result.TagUsage.Count(item => item.UsageCount == 0)}**");
        builder.AppendLine();
        builder.AppendLine("## Top Tag Usage");
        builder.AppendLine();

        foreach (var tag in result.TagUsage.Take(80))
        {
            builder.AppendLine($"- {tag.Name}: **{tag.UsageCount}** (`{tag.QualifiedPath}`)");
        }

        return builder.ToString();
    }

    private static string BuildUnusedMarkdown(AnalysisResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Unused Object Analysis");
        builder.AppendLine();
        builder.AppendLine($"Unused candidates: **{result.UnusedObjects.Count}**");
        builder.AppendLine();

        if (result.UnusedObjects.Count == 0)
        {
            builder.AppendLine("No unused candidate objects were detected with the current dependency metadata.");
            return builder.ToString();
        }

        foreach (var item in result.UnusedObjects.Take(120))
        {
            builder.AppendLine($"- {item.ObjectType}: `{item.QualifiedPath}`");
        }

        return builder.ToString();
    }

    private static string BuildNodeId(TiaProjectObjectNode node) =>
        string.IsNullOrWhiteSpace(node.QualifiedPath) ? node.Name : node.QualifiedPath;

    private static IEnumerable<string> ParseDependencies(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return Array.Empty<string>();
        }

        return DependencyKeys
            .Where(metadata.ContainsKey)
            .SelectMany(key => SplitValues(metadata[key]))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitValues(string raw)
    {
        return raw.Split(DependencySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static Node? ResolveTarget(string token, IReadOnlyDictionary<string, Node> byId, IReadOnlyDictionary<string, Node> byName)
    {
        if (byId.TryGetValue(token, out var directId))
        {
            return directId;
        }

        if (byName.TryGetValue(token, out var directName))
        {
            return directName;
        }

        return byId.Values.FirstOrDefault(node => node.Source.QualifiedPath.EndsWith($"/{token}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTagNode(TiaProjectObjectNode node) =>
        node.ObjectType.Contains("Tag", StringComparison.OrdinalIgnoreCase)
        || node.QualifiedPath.Contains("/Tags/", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnusedCandidate(TiaProjectObjectNode node) =>
        node.ObjectType.Contains("Block", StringComparison.OrdinalIgnoreCase)
        || node.ObjectType.Contains("OB", StringComparison.OrdinalIgnoreCase)
        || node.ObjectType.Contains("FB", StringComparison.OrdinalIgnoreCase)
        || node.ObjectType.Contains("FC", StringComparison.OrdinalIgnoreCase)
        || node.ObjectType.Contains("DB", StringComparison.OrdinalIgnoreCase)
        || node.ObjectType.Contains("Tag", StringComparison.OrdinalIgnoreCase)
        || node.ObjectType.Contains("UDT", StringComparison.OrdinalIgnoreCase)
        || node.ObjectType.Contains("Screen", StringComparison.OrdinalIgnoreCase)
        || node.ObjectType.Contains("Faceplate", StringComparison.OrdinalIgnoreCase);

    private static bool IsEntryPoint(TiaProjectObjectNode node)
    {
        if (node.Metadata is not null
            && node.Metadata.TryGetValue("IsEntryPoint", out var raw)
            && bool.TryParse(raw, out var isEntryPoint)
            && isEntryPoint)
        {
            return true;
        }

        return node.ObjectType.Equals("OB", StringComparison.OrdinalIgnoreCase)
            || node.Name.Contains("Main", StringComparison.OrdinalIgnoreCase);
    }

    private static Node SelectPreferredNode(IEnumerable<Node> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.Source.Metadata?.Count ?? 0)
            .ThenByDescending(candidate => candidate.Source.Depth)
            .First();
    }

    private sealed record Node(TiaProjectObjectNode Source, string Id);

    private sealed record TagUsageItem(string Name, string QualifiedPath, int UsageCount);

    private sealed record UnusedItem(string Name, string ObjectType, string QualifiedPath);

    private sealed record AnalysisResult(
        IReadOnlyList<TagUsageItem> TagUsage,
        IReadOnlyList<UnusedItem> UnusedObjects,
        object TagUsagePayload,
        object UnusedPayload);
}
