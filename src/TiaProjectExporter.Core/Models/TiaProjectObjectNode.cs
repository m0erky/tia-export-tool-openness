namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Represents a discovered TIA project object in a traversal-friendly form.
/// </summary>
public sealed record TiaProjectObjectNode(
    string ObjectType,
    string Name,
    string QualifiedPath,
    int Depth,
    IReadOnlyDictionary<string, string>? Metadata = null);

