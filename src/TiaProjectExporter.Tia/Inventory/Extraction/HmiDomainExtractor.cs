using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts HMI-related runtime objects.
/// </summary>
public sealed class HmiDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "HMI";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) =>
        runtimeTypeName.Contains("Hmi", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Screen", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Faceplate", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Alarm", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Recipe", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Script", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        var runtimeTypeName = runtimeNode.GetType().Name;

        if (!CanHandle(runtimeTypeName))
        {
            return null;
        }

        var objectType = runtimeTypeName.Contains("Faceplate", StringComparison.OrdinalIgnoreCase)
            ? "Faceplate"
            : runtimeTypeName.Contains("Screen", StringComparison.OrdinalIgnoreCase)
                ? "Screen"
                : "HMI";

        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeNode.GetType().Name;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain
        };

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
