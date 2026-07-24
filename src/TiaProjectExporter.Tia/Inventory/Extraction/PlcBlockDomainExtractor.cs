using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts PLC block-like objects (OB/FB/FC/DB/Block).
/// </summary>
public sealed class PlcBlockDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "PLC.Blocks";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) => Classify(runtimeTypeName) is not null;

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        var objectType = Classify(runtimeNode.GetType().Name);
        if (objectType is null)
        {
            return null;
        }

        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeNode.GetType().Name;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain
        };

        var calls = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Calls", "CalledBlocks", "ReferencedBlocks", "UsedBlocks");
        if (calls.Length > 0)
        {
            metadata["Calls"] = string.Join(", ", calls);
        }

        var dependencies = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "References", "Dependencies", "UsedTypes", "ReferencedTags");
        if (dependencies.Length > 0)
        {
            metadata["Dependencies"] = string.Join(", ", dependencies);
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }

    private static string? Classify(string runtimeTypeName)
    {
        if (runtimeTypeName.Contains("OB", StringComparison.OrdinalIgnoreCase))
        {
            return "OB";
        }

        if (runtimeTypeName.Contains("FB", StringComparison.OrdinalIgnoreCase))
        {
            return "FB";
        }

        if (runtimeTypeName.Contains("FC", StringComparison.OrdinalIgnoreCase))
        {
            return "FC";
        }

        if (runtimeTypeName.Contains("DB", StringComparison.OrdinalIgnoreCase))
        {
            return "DB";
        }

        if (runtimeTypeName.Contains("Block", StringComparison.OrdinalIgnoreCase))
        {
            return "Block";
        }

        return null;
    }
}
