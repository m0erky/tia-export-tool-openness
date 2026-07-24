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

        var dataType = ReflectionNodeIntrospection.TryReadString(runtimeNode, "DataType")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "TypeName")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Datatype");
        if (!string.IsNullOrWhiteSpace(dataType))
        {
            metadata["DataType"] = dataType;
        }

        var address = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Address")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "LogicalAddress");
        if (!string.IsNullOrWhiteSpace(address))
        {
            metadata["Address"] = address;
        }

        var initialValue = ReflectionNodeIntrospection.TryReadString(runtimeNode, "InitialValue")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "StartValue")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DefaultValue");
        if (!string.IsNullOrWhiteSpace(initialValue))
        {
            metadata["InitialValue"] = initialValue;
        }

        var referencedTags = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "ReferencedTags", "UsedTags", "TagUsage");
        if (referencedTags.Length > 0)
        {
            metadata["ReferencedTags"] = string.Join(", ", referencedTags);
            metadata["TagUsage"] = string.Join(", ", referencedTags);
        }

        if (objectType.Equals("TagTable", StringComparison.OrdinalIgnoreCase))
        {
            var tags = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Tags", "TagList", "Entries");
            if (tags.Length > 0)
            {
                metadata["TagCount"] = tags.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                metadata["Dependencies"] = string.Join(", ", tags);
            }
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
