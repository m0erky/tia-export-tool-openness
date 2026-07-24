using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts hardware/runtime module objects from Openness reflection nodes.
/// </summary>
public sealed class HardwareDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "Hardware";

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

        var interfaces = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Interfaces", "NetworkInterfaces", "Ports");
        if (interfaces.Length > 0)
        {
            metadata["Interfaces"] = string.Join(", ", interfaces);
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }

    private static string? Classify(string runtimeTypeName)
    {
        if (runtimeTypeName.Contains("Device", StringComparison.OrdinalIgnoreCase))
        {
            return "Device";
        }

        if (runtimeTypeName.Contains("Cpu", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("CPU", StringComparison.OrdinalIgnoreCase))
        {
            return "CPU";
        }

        if (runtimeTypeName.Contains("Module", StringComparison.OrdinalIgnoreCase))
        {
            return "Module";
        }

        if (runtimeTypeName.Contains("Rack", StringComparison.OrdinalIgnoreCase))
        {
            return "Rack";
        }

        if (runtimeTypeName.Contains("Interface", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Port", StringComparison.OrdinalIgnoreCase))
        {
            return "Interface";
        }

        return null;
    }
}
