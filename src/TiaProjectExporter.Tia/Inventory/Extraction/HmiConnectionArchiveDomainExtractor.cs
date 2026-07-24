using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts HMI connection/archive-oriented runtime nodes.
/// </summary>
public sealed class HmiConnectionArchiveDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "HMI";

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
            ["Domain"] = Domain,
            ["HmiSubdomain"] = objectType
        };

        var endpoints = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Connections", "ConnectedTo", "Devices", "Archives");
        if (endpoints.Length > 0)
        {
            metadata["Dependencies"] = string.Join(", ", endpoints);
            metadata["EndpointCount"] = endpoints.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var protocol = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Protocol")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Type")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Driver");
        if (!string.IsNullOrWhiteSpace(protocol))
        {
            metadata["Protocol"] = protocol;
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }

    private static string? Classify(string runtimeTypeName)
    {
        if (runtimeTypeName.Contains("Archive", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Historian", StringComparison.OrdinalIgnoreCase))
        {
            return "Archive";
        }

        if (runtimeTypeName.Contains("Connection", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Connector", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Channel", StringComparison.OrdinalIgnoreCase))
        {
            return "Connection";
        }

        return null;
    }
}
