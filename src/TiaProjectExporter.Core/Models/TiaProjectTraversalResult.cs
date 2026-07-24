namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Raw traversal output returned from a Siemens Openness adapter.
/// </summary>
public sealed record TiaProjectTraversalResult(
    string? ProjectName,
    string ProjectPath,
    IReadOnlyList<TiaProjectObjectNode> Objects,
    IReadOnlyList<ExportIssue> Issues);
