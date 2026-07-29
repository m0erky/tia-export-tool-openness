namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Structured inventory of a TIA project used to drive repository export.
/// </summary>
public sealed record TiaProjectInventory(
    TiaInventoryStatus Status,
    string? ProjectName,
    string? ProjectPath,
    IReadOnlyList<TiaProjectObjectNode> Objects,
    IReadOnlyList<ExportIssue> Issues,
    InventoryDeduplicationSummary? DeduplicationSummary = null);
