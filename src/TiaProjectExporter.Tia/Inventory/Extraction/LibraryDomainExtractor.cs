using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts library-related runtime objects.
/// </summary>
public sealed class LibraryDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "Libraries";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) =>
        runtimeTypeName.Contains("Library", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("MasterCopy", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("TypeVersion", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        if (!CanHandle(runtimeNode.GetType().Name))
        {
            return null;
        }

        var objectType = runtimeNode.GetType().Name.Contains("MasterCopy", StringComparison.OrdinalIgnoreCase)
            ? "LibraryMasterCopy"
            : runtimeNode.GetType().Name.Contains("Type", StringComparison.OrdinalIgnoreCase)
                ? "LibraryType"
                : "Library";

        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeNode.GetType().Name;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain
        };

        var versions = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Versions", "TypeVersions");
        if (versions.Length > 0)
        {
            metadata["Versions"] = string.Join(", ", versions);
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
