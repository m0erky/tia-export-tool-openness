namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Summarizes inventory deduplication after canonical path processing.
/// </summary>
public sealed record InventoryDeduplicationSummary(
    int InputObjects,
    int RemovedDuplicates,
    int UniqueObjects,
    IReadOnlyList<InventoryDuplicateGroup> TopDuplicateGroups)
{
    /// <summary>
    /// Returns an empty summary for inventories without deduplication data.
    /// </summary>
    public static InventoryDeduplicationSummary Empty { get; } =
        new(0, 0, 0, Array.Empty<InventoryDuplicateGroup>());
}

/// <summary>
/// Represents one duplicate group detected during inventory deduplication.
/// </summary>
public sealed record InventoryDuplicateGroup(
    string ObjectType,
    string CanonicalQualifiedPath,
    int Count);

