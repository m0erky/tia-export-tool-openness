using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts technology, motion, PID, and safety related runtime objects.
/// </summary>
public sealed class TechnologyDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "Technology";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) =>
        runtimeTypeName.Contains("Technology", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Motion", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Axis", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Safety", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Pid", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Control", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        if (!CanHandle(runtimeNode.GetType().Name))
        {
            return null;
        }

        var runtimeType = runtimeNode.GetType().Name;
        var objectType = runtimeType.Contains("Safety", StringComparison.OrdinalIgnoreCase)
            ? "Safety"
            : runtimeType.Contains("Pid", StringComparison.OrdinalIgnoreCase)
                ? "PID"
                : runtimeType.Contains("Motion", StringComparison.OrdinalIgnoreCase) || runtimeType.Contains("Axis", StringComparison.OrdinalIgnoreCase)
                    ? "Motion"
                    : "Technology";

        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeType;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain
        };

        var references = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Axes", "Drives", "References", "Dependencies");
        if (references.Length > 0)
        {
            metadata["Dependencies"] = string.Join(", ", references);
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
