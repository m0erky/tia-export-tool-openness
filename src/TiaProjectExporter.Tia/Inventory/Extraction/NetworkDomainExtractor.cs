using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts network/topology-related objects from Openness reflection nodes.
/// </summary>
public sealed class NetworkDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "Network";

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

        metadata["TopologyDepth"] = depth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["NetworkType"] = objectType;

        var references = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Connections", "ConnectedTo", "Nodes", "Subnets", "IoSystems");
        if (references.Length > 0)
        {
            metadata["Dependencies"] = string.Join(", ", references);
            metadata["EndpointCount"] = references.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var subnet = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Subnet")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "SubnetName")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Address");
        if (!string.IsNullOrWhiteSpace(subnet))
        {
            metadata["Subnet"] = subnet;
        }

        var protocol = ResolveProtocol(runtimeNode.GetType().Name, objectType);
        metadata["Protocol"] = protocol;

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }

    private static string? Classify(string runtimeTypeName)
    {
        if (runtimeTypeName.Contains("Profinet", StringComparison.OrdinalIgnoreCase))
        {
            return "PROFINET";
        }

        if (runtimeTypeName.Contains("Profibus", StringComparison.OrdinalIgnoreCase))
        {
            return "PROFIBUS";
        }

        if (runtimeTypeName.Contains("Connection", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Connect", StringComparison.OrdinalIgnoreCase))
        {
            return "Connection";
        }

        if (runtimeTypeName.Contains("Subnet", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Network", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Topology", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("IoSystem", StringComparison.OrdinalIgnoreCase))
        {
            return "Network";
        }

        return null;
    }

    private static string ResolveProtocol(string runtimeTypeName, string objectType)
    {
        if (runtimeTypeName.Contains("Profinet", StringComparison.OrdinalIgnoreCase) || objectType == "PROFINET")
        {
            return "PROFINET";
        }

        if (runtimeTypeName.Contains("Profibus", StringComparison.OrdinalIgnoreCase) || objectType == "PROFIBUS")
        {
            return "PROFIBUS";
        }

        if (runtimeTypeName.Contains("Ethernet", StringComparison.OrdinalIgnoreCase))
        {
            return "Ethernet";
        }

        return "Generic";
    }
}
