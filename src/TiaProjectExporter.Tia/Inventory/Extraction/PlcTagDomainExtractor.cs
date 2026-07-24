using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts PLC tag and tag-table-like objects.
/// </summary>
public sealed class PlcTagDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "PLC.Tags";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) =>
        runtimeTypeName.Contains("Tag", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("TagTable", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        var runtimeType = runtimeNode.GetType().Name;

        if (!CanHandle(runtimeType))
        {
            return null;
        }

        var objectType = runtimeType.Contains("Table", StringComparison.OrdinalIgnoreCase) ? "TagTable" : "Tag";
        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeNode.GetType().Name;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain
        };

        var referencedTags = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "ReferencedTags", "UsedTags", "TagUsage");
        if (referencedTags.Length > 0)
        {
            metadata["ReferencedTags"] = string.Join(", ", referencedTags);
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
