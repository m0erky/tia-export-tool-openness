using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts PLC data type objects including UDT-like runtime entities.
/// </summary>
public sealed class PlcDataTypeDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "PLC.DataTypes";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) =>
        runtimeTypeName.Contains("UDT", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("DataType", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Type", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        var runtimeType = runtimeNode.GetType().Name;

        if (!CanHandle(runtimeType))
        {
            return null;
        }

        var objectType = runtimeType.Contains("UDT", StringComparison.OrdinalIgnoreCase) ? "UDT" : "DataType";
        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeNode.GetType().Name;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain
        };

        var references = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "References", "Dependencies", "UsedTypes");
        if (references.Length > 0)
        {
            metadata["Dependencies"] = string.Join(", ", references);
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
