using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts metadata-oriented runtime nodes (versioning, texts, language resources, comments).
/// </summary>
public sealed class MetadataDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "Metadata";

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

        var language = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Language")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Culture")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Locale");
        if (!string.IsNullOrWhiteSpace(language))
        {
            metadata["Language"] = language;
        }

        var version = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Version")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Revision")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Build");
        if (!string.IsNullOrWhiteSpace(version))
        {
            metadata["Version"] = version;
        }

        var sources = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "References", "TextResources", "Languages");
        if (sources.Length > 0)
        {
            metadata["Dependencies"] = string.Join(", ", sources);
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }

    private static string? Classify(string runtimeTypeName)
    {
        if (runtimeTypeName.Contains("Version", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Revision", StringComparison.OrdinalIgnoreCase))
        {
            return "VersionInfo";
        }

        if (runtimeTypeName.Contains("Language", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Culture", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Locale", StringComparison.OrdinalIgnoreCase))
        {
            return "LanguageResource";
        }

        if (runtimeTypeName.Contains("Metadata", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Comment", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Text", StringComparison.OrdinalIgnoreCase))
        {
            return "MetadataEntry";
        }

        return null;
    }
}
