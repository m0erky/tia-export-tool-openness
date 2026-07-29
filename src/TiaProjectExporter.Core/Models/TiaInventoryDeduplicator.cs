namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Deduplicates inventory objects by canonical path and object type.
/// </summary>
public static class TiaInventoryDeduplicator
{
    /// <summary>
    /// Deduplicates objects using key: (ObjectType, CanonicalQualifiedPath).
    /// </summary>
    /// <remarks>
    /// Conflict resolution priority:
    /// 1) typed extraction
    /// 2) host PLC model extraction
    /// 3) reflection-based extraction
    /// Then by richer content (export XML/source text presence).
    /// </remarks>
    public static DeduplicationResult Deduplicate(IReadOnlyList<TiaProjectObjectNode> input)
    {
        if (input.Count == 0)
        {
            return new DeduplicationResult(Array.Empty<TiaProjectObjectNode>(), InventoryDeduplicationSummary.Empty);
        }

        var groups = input
            .Select((node, index) => new
            {
                Node = node,
                Index = index,
                CanonicalPath = QualifiedPathCanonicalizer.Canonicalize(node.QualifiedPath)
            })
            .GroupBy(entry => (entry.Node.ObjectType, entry.CanonicalPath), entry => entry)
            .ToArray();

        var duplicateGroups = groups
            .Where(group => group.Count() > 1)
            .Select(group => new InventoryDuplicateGroup(
                ObjectType: group.Key.ObjectType,
                CanonicalQualifiedPath: group.Key.CanonicalPath,
                Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.ObjectType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.CanonicalQualifiedPath, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        var deduplicated = new List<TiaProjectObjectNode>(groups.Length);

        foreach (var group in groups)
        {
            var entries = group.ToArray();
            var selected = entries
                .OrderByDescending(entry => GetExtractionPriority(entry.Node))
                .ThenByDescending(entry => GetContentPriority(entry.Node))
                .ThenBy(entry => entry.Node.Depth)
                .ThenBy(entry => entry.Node.QualifiedPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Index)
                .First();

            var canonicalPath = group.Key.CanonicalPath;
            var originalPaths = entries
                .Select(entry => entry.Node.QualifiedPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var metadata = selected.Node.Metadata is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(selected.Node.Metadata, StringComparer.OrdinalIgnoreCase);

            metadata["CanonicalQualifiedPath"] = canonicalPath;
            metadata["QualifiedPathCanonicalized"] = bool.TrueString;
            metadata["OriginalQualifiedPaths"] = string.Join(" | ", originalPaths);
            metadata["DeduplicationDuplicateCount"] = entries.Length.ToString();

            if (entries.Length > 1)
            {
                metadata["DeduplicationConflictRule"] = "typed>host-plc-model>reflection; then richer content";
            }

            deduplicated.Add(new TiaProjectObjectNode(
                selected.Node.ObjectType,
                selected.Node.Name,
                canonicalPath,
                selected.Node.Depth,
                metadata));
        }

        var summary = new InventoryDeduplicationSummary(
            InputObjects: input.Count,
            RemovedDuplicates: input.Count - deduplicated.Count,
            UniqueObjects: deduplicated.Count,
            TopDuplicateGroups: duplicateGroups);

        return new DeduplicationResult(deduplicated, summary);
    }

    private static int GetExtractionPriority(TiaProjectObjectNode node)
    {
        if (IsTrue(node.Metadata, "ExtractedByTypedExtractor"))
        {
            return 300;
        }

        var strategy = GetMetadata(node.Metadata, "ExtractionStrategy");

        if (strategy.Contains("HostPlcModel", StringComparison.OrdinalIgnoreCase)
            || strategy.Contains("HostPreview", StringComparison.OrdinalIgnoreCase)
            || strategy.Contains("HostPreviewBlockFallback", StringComparison.OrdinalIgnoreCase))
        {
            return 200;
        }

        if (strategy.Contains("Reflection", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        return 150;
    }

    private static int GetContentPriority(TiaProjectObjectNode node)
    {
        var score = 0;

        if (HasText(node.Metadata, "Content.ExportXml"))
        {
            score += 10;
        }

        if (HasText(node.Metadata, "Content.SourceText"))
        {
            score += 10;
        }

        if (HasText(node.Metadata, "BlockNumber"))
        {
            score += 1;
        }

        return score;
    }

    private static bool HasText(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null
        && metadata.TryGetValue(key, out var value)
        && !string.IsNullOrWhiteSpace(value);

    private static bool IsTrue(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null
        && metadata.TryGetValue(key, out var value)
        && bool.TryParse(value, out var parsed)
        && parsed;

    private static string GetMetadata(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null
        && metadata.TryGetValue(key, out var value)
            ? value
            : string.Empty;

    /// <summary>
    /// Result object for deduplication.
    /// </summary>
    public sealed record DeduplicationResult(
        IReadOnlyList<TiaProjectObjectNode> Objects,
        InventoryDeduplicationSummary Summary);
}

