namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Captures the export result for an individual object or logical unit.
/// </summary>
public sealed record ExportedObjectResult(
    string ObjectType,
    string Identifier,
    ExportObjectStatus Status,
    string? Message = null);

