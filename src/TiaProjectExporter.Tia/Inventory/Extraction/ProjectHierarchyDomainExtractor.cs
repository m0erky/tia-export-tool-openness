using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts project tree hierarchy objects such as folders and device groups.
/// </summary>
public sealed class ProjectHierarchyDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "Project";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) =>
        runtimeTypeName.Contains("Group", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Folder", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("ProjectTree", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Container", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Subfolder", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        if (!CanHandle(runtimeNode.GetType().Name))
        {
            return null;
        }

        var runtimeType = runtimeNode.GetType().Name;
        var objectType = runtimeType.Contains("DeviceGroup", StringComparison.OrdinalIgnoreCase)
            ? "DeviceGroup"
            : runtimeType.Contains("Folder", StringComparison.OrdinalIgnoreCase)
                ? "Folder"
                : "ProjectGroup";

        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeType;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain,
            ["Hierarchy"] = "true"
        };

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
