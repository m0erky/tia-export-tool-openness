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

        metadata["HierarchyDepth"] = depth.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var lastSeparator = qualifiedPath.LastIndexOf('/');
        if (lastSeparator > 0)
        {
            metadata["ParentPath"] = qualifiedPath[..lastSeparator];
        }

        var moduleCategory = ResolveModuleCategory(runtimeNode.GetType().Name);
        metadata["ModuleCategory"] = moduleCategory;

        var interfaces = ReflectionNodeIntrospection.ExtractNamedReferences(runtimeNode, "Interfaces", "NetworkInterfaces", "Ports");
        if (interfaces.Length > 0)
        {
            metadata["Interfaces"] = string.Join(", ", interfaces);
            metadata["InterfaceCount"] = interfaces.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var position = ReflectionNodeIntrospection.TryReadString(runtimeNode, "PositionNumber")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Slot")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "OrderNumber");
        if (!string.IsNullOrWhiteSpace(position))
        {
            metadata["Position"] = position;
        }

        var hardwareIdentifier = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Identifier")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "HwIdentifier")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Id");
        if (!string.IsNullOrWhiteSpace(hardwareIdentifier))
        {
            metadata["HardwareIdentifier"] = hardwareIdentifier;
        }

        var address = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Address")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "LogicalAddress")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "StartAddress");
        if (!string.IsNullOrWhiteSpace(address))
        {
            metadata["Address"] = address;
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }

    private static string? Classify(string runtimeTypeName)
    {
        if (runtimeTypeName.Contains("DeviceItemImpl", StringComparison.OrdinalIgnoreCase))
        {
            return "DeviceItem";
        }

        if (runtimeTypeName.Contains("HwIdentifier", StringComparison.OrdinalIgnoreCase))
        {
            return "HardwareIdentifier";
        }

        if (runtimeTypeName.Equals("Address", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("HwAddress", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("HardwareAddress", StringComparison.OrdinalIgnoreCase))
        {
            return "HardwareAddress";
        }

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

    private static string ResolveModuleCategory(string runtimeTypeName)
    {
        if (runtimeTypeName.Contains("Cpu", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("CPU", StringComparison.OrdinalIgnoreCase))
        {
            return "Controller";
        }

        if (runtimeTypeName.Contains("Interface", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("Port", StringComparison.OrdinalIgnoreCase))
        {
            return "Interface";
        }

        if (runtimeTypeName.Contains("Rack", StringComparison.OrdinalIgnoreCase))
        {
            return "Rack";
        }

        if (runtimeTypeName.Contains("Device", StringComparison.OrdinalIgnoreCase))
        {
            return "Device";
        }

        return "Module";
    }
}
