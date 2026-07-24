using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts user and audit related runtime objects.
/// </summary>
public sealed class UsersAuditDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "UsersAudit";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) =>
        runtimeTypeName.Contains("User", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Audit", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Permission", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Role", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        if (!CanHandle(runtimeNode.GetType().Name))
        {
            return null;
        }

        var runtimeType = runtimeNode.GetType().Name;
        var objectType = runtimeType.Contains("Audit", StringComparison.OrdinalIgnoreCase)
            ? "Audit"
            : runtimeType.Contains("Role", StringComparison.OrdinalIgnoreCase)
                ? "Role"
                : "User";

        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeNode.GetType().Name;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain
        };

        var permissions = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Permissions", "Roles", "AssignedRoles");
        if (permissions.Length > 0)
        {
            metadata["Permissions"] = string.Join(", ", permissions);
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
