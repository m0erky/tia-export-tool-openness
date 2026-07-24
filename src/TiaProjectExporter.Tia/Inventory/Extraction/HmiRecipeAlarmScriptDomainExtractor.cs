using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts HMI recipes, alarms, and scripts.
/// </summary>
public sealed class HmiRecipeAlarmScriptDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "HMI";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName)
    {
        if (runtimeTypeName.Contains("Recipe", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Script", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Archive", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return runtimeTypeName.Contains("Alarm", StringComparison.OrdinalIgnoreCase)
            && (runtimeTypeName.Contains("Hmi", StringComparison.OrdinalIgnoreCase)
                || runtimeTypeName.Contains("Wincc", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        var runtimeType = runtimeNode.GetType().Name;

        if (!CanHandle(runtimeType))
        {
            return null;
        }

        var objectType = runtimeType.Contains("Recipe", StringComparison.OrdinalIgnoreCase)
            ? "Recipe"
            : runtimeType.Contains("Alarm", StringComparison.OrdinalIgnoreCase)
                ? "Alarm"
                : runtimeType.Contains("Archive", StringComparison.OrdinalIgnoreCase)
                    ? "Archive"
                    : "Script";

        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeType;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain,
            ["HmiSubdomain"] = objectType
        };

        var dependencies = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Connections", "References", "UsedTags", "Scripts");
        if (dependencies.Length > 0)
        {
            metadata["Dependencies"] = string.Join(", ", dependencies);
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
