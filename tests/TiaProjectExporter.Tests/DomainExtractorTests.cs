using TiaProjectExporter.Tia.Inventory.Extraction;

namespace TiaProjectExporter.Tests;

public sealed class DomainExtractorTests
{
    [Fact]
    public void HardwareDomainExtractor_ExtractsModuleMetadata()
    {
        var extractor = new HardwareDomainExtractor();
        var runtime = new SimaticModuleRuntime
        {
            Name = "DI_16x24VDC",
            Interfaces = new[] { new NamedNode("PN1"), new NamedNode("PN2") },
            Comment = "Digital input module",
            Slot = 3
        };

        var node = extractor.TryExtract(runtime, "Project/Hardware/Rack_1/DI_16x24VDC", 3);

        Assert.NotNull(node);
        Assert.Equal("Module", node.ObjectType);
        Assert.Equal("Hardware", node.Metadata?["Domain"]);
        Assert.Contains("PN1", node.Metadata?["Interfaces"]);
        Assert.Equal("2", node.Metadata?["InterfaceCount"]);
        Assert.Equal("3", node.Metadata?["HierarchyDepth"]);
        Assert.Equal("Project/Hardware/Rack_1", node.Metadata?["ParentPath"]);
        Assert.Equal("Module", node.Metadata?["ModuleCategory"]);
        Assert.Equal("3", node.Metadata?["Position"]);
    }

    [Fact]
    public void NetworkDomainExtractor_ExtractsNetworkDependencies()
    {
        var extractor = new NetworkDomainExtractor();
        var runtime = new ProfinetConnectionRuntime
        {
            Name = "PN_Line_1",
            Connections = new[] { new NamedNode("PLC_1"), new NamedNode("HMI_1") },
            SubnetName = "PN_SUBNET_A"
        };

        var node = extractor.TryExtract(runtime, "Project/Network/PN_Line_1", 2);

        Assert.NotNull(node);
        Assert.Equal("PROFINET", node.ObjectType);
        Assert.Equal("Network", node.Metadata?["Domain"]);
        Assert.Contains("PLC_1", node.Metadata?["Dependencies"]);
        Assert.Equal("2", node.Metadata?["TopologyDepth"]);
        Assert.Equal("PROFINET", node.Metadata?["NetworkType"]);
        Assert.Equal("2", node.Metadata?["EndpointCount"]);
        Assert.Equal("PN_SUBNET_A", node.Metadata?["Subnet"]);
        Assert.Equal("PROFINET", node.Metadata?["Protocol"]);
    }

    private sealed class NamedNode
    {
        public NamedNode(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class SimaticModuleRuntime
    {
        public string? Name { get; init; }

        public IEnumerable<NamedNode>? Interfaces { get; init; }

        public string? Comment { get; init; }

        public int Slot { get; init; }
    }

    private sealed class ProfinetConnectionRuntime
    {
        public string? Name { get; init; }

        public IEnumerable<NamedNode>? Connections { get; init; }

        public string? SubnetName { get; init; }
    }
}
