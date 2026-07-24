using TiaProjectExporter.Tia.Inventory.Extraction;

namespace TiaProjectExporter.Tests;

public sealed class PlcDomainExtractorTests
{
    [Fact]
    public void PlcBlockDomainExtractor_ExtractsObWithEntryPointAndRelationships()
    {
        var extractor = new PlcBlockDomainExtractor();
        var runtime = new ObRuntime
        {
            Name = "OB1",
            ProgrammingLanguage = "LAD",
            Number = 1,
            CalledBlocks = new[] { new NamedNode("FB_Main") },
            ReferencedTags = new[] { new NamedNode("Tag_A") },
            Dependencies = new[] { new NamedNode("UDT_Motor") }
        };

        var node = extractor.TryExtract(runtime, "Project/PLC/Blocks/OB1", 3);

        Assert.NotNull(node);
        Assert.Equal("OB", node.ObjectType);
        Assert.Equal("true", node.Metadata?["IsEntryPoint"]);
        Assert.Equal("LAD", node.Metadata?["Language"]);
        Assert.Equal("1", node.Metadata?["BlockNumber"]);
        Assert.Contains("FB_Main", node.Metadata?["Calls"]);
        Assert.Contains("Tag_A", node.Metadata?["TagUsage"]);
        Assert.Contains("UDT_Motor", node.Metadata?["Dependencies"]);
        Assert.Contains("Tag_A", node.Metadata?["Dependencies"]);
    }

    [Fact]
    public void PlcBlockDomainExtractor_ClassifiesInstanceDb()
    {
        var extractor = new PlcBlockDomainExtractor();
        var runtime = new InstanceDbRuntime
        {
            Name = "IDB_FB_Main",
            DataType = "FB_Main"
        };

        var node = extractor.TryExtract(runtime, "Project/PLC/Blocks/IDB_FB_Main", 3);

        Assert.NotNull(node);
        Assert.Equal("InstanceDB", node.ObjectType);
        Assert.Equal("FB_Main", node.Metadata?["DataType"]);
    }

    [Fact]
    public void PlcTagDomainExtractor_ExtractsTagAndTagTableMetadata()
    {
        var extractor = new PlcTagDomainExtractor();

        var tagNode = extractor.TryExtract(
            new PlcTagRuntime
            {
                Name = "MotorSpeed",
                DataType = "Real",
                Address = "%MD100",
                InitialValue = "0.0",
                UsedTags = new[] { new NamedNode("Setpoint") }
            },
            "Project/PLC/Tags/MotorSpeed",
            3);

        Assert.NotNull(tagNode);
        Assert.Equal("Tag", tagNode.ObjectType);
        Assert.Equal("Real", tagNode.Metadata?["DataType"]);
        Assert.Equal("%MD100", tagNode.Metadata?["Address"]);
        Assert.Equal("0.0", tagNode.Metadata?["InitialValue"]);
        Assert.Contains("Setpoint", tagNode.Metadata?["TagUsage"]);

        var tableNode = extractor.TryExtract(
            new PlcTagTableRuntime
            {
                Name = "GlobalTags",
                Tags = new[] { new NamedNode("MotorSpeed"), new NamedNode("Setpoint") }
            },
            "Project/PLC/TagTables/GlobalTags",
            2);

        Assert.NotNull(tableNode);
        Assert.Equal("TagTable", tableNode.ObjectType);
        Assert.Equal("2", tableNode.Metadata?["TagCount"]);
        Assert.Contains("MotorSpeed", tableNode.Metadata?["Dependencies"]);
    }

    private sealed class NamedNode
    {
        public NamedNode(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class ObRuntime
    {
        public string? Name { get; init; }

        public string? ProgrammingLanguage { get; init; }

        public int Number { get; init; }

        public IEnumerable<NamedNode>? CalledBlocks { get; init; }

        public IEnumerable<NamedNode>? ReferencedTags { get; init; }

        public IEnumerable<NamedNode>? Dependencies { get; init; }
    }

    private sealed class InstanceDbRuntime
    {
        public string? Name { get; init; }

        public string? DataType { get; init; }
    }

    private sealed class PlcTagRuntime
    {
        public string? Name { get; init; }

        public string? DataType { get; init; }

        public string? Address { get; init; }

        public string? InitialValue { get; init; }

        public IEnumerable<NamedNode>? UsedTags { get; init; }
    }

    private sealed class PlcTagTableRuntime
    {
        public string? Name { get; init; }

        public IEnumerable<NamedNode>? Tags { get; init; }
    }
}
