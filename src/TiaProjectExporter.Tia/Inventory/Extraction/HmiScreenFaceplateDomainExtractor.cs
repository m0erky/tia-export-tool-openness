using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts HMI screens and faceplates.
/// </summary>
public sealed class HmiScreenFaceplateDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "HMI";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) =>
        runtimeTypeName.Contains("Screen", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Faceplate", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Template", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        var runtimeType = runtimeNode.GetType().Name;

        if (!CanHandle(runtimeType))
        {
            return null;
        }

        var objectType = runtimeType.Contains("Faceplate", StringComparison.OrdinalIgnoreCase)
            ? "Faceplate"
            : runtimeType.Contains("Template", StringComparison.OrdinalIgnoreCase)
                ? "Template"
                : "Screen";

        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeType;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain,
            ["HmiSubdomain"] = objectType
        };

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
