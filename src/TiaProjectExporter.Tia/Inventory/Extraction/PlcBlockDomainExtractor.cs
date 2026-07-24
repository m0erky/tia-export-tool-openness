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

        metadata["BlockType"] = objectType;

        if (objectType.Equals("OB", StringComparison.OrdinalIgnoreCase))
        {
            metadata["IsEntryPoint"] = "true";
        }

        var language = ReflectionNodeIntrospection.TryReadString(runtimeNode, "ProgrammingLanguage")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Language");
        if (!string.IsNullOrWhiteSpace(language))
        {
            metadata["Language"] = language;
        }

        var blockNumber = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Number")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "BlockNumber")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Id");
        if (!string.IsNullOrWhiteSpace(blockNumber))
        {
            metadata["BlockNumber"] = blockNumber;
        }

        var calls = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Calls", "CalledBlocks", "ReferencedBlocks", "UsedBlocks", "BlockCalls", "InvokedBlocks");
        if (calls.Length > 0)
        {
            metadata["Calls"] = string.Join(", ", calls);
        }

        var tagUsage = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "TagUsage", "UsedTags", "ReferencedTags");
        if (tagUsage.Length > 0)
        {
            metadata["TagUsage"] = string.Join(", ", tagUsage);
        }

        var dataType = ReflectionNodeIntrospection.TryReadString(runtimeNode, "DataType")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "TypeName")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "InstanceOf");
        if (!string.IsNullOrWhiteSpace(dataType))
        {
            metadata["DataType"] = dataType;
        }

        var dependencies = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "References", "Dependencies", "UsedTypes", "ReferencedTags");
        var mergedDependencies = dependencies
            .Concat(tagUsage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (mergedDependencies.Length > 0)
        {
            metadata["Dependencies"] = string.Join(", ", mergedDependencies);
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

        if (runtimeTypeName.Contains("InstanceDB", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("InstanceDb", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("IDB", StringComparison.OrdinalIgnoreCase))
        {
            return "InstanceDB";
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
