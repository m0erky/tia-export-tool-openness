namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Progress update emitted during export execution.
/// </summary>
public sealed record ExportProgressUpdate(
    string CurrentStage,
    string CurrentObject,
    int ProcessedItems,
    int? TotalItems,
    TimeSpan? EstimatedRemaining);

