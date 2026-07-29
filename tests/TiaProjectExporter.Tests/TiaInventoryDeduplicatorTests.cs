using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tests;

public sealed class TiaInventoryDeduplicatorTests
{
    [Fact]
    public void Deduplicate_UsesCanonicalObjectTypePathKey_AndRemovesVariantDuplicates()
    {
        var input = new[]
        {
            new TiaProjectObjectNode(
                "FB",
                "Block_1",
                "Project/DeviceItemImpl/DeviceItemImpl/BlockGroup/Blocks/Block_1",
                2,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ExtractionStrategy"] = "HostReflection",
                    ["Content.SourceText"] = "legacy"
                }),
            new TiaProjectObjectNode(
                "FB",
                "Block_1",
                "Project/DeviceItemImpl/BlockGroup/Block_1",
                2,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ExtractedByTypedExtractor"] = "true",
                    ["ExtractionStrategy"] = "TypedExtractor",
                    ["Content.SourceText"] = "typed"
                })
        };

        var result = TiaInventoryDeduplicator.Deduplicate(input);

        Assert.Single(result.Objects);
        var node = result.Objects[0];
        Assert.Equal("Project/DeviceItemImpl/BlockGroup/Block_1", node.QualifiedPath);
        Assert.Equal("true", node.Metadata!["ExtractedByTypedExtractor"]);
        Assert.Equal("2", node.Metadata!["DeduplicationDuplicateCount"]);
        Assert.Contains("DeviceItemImpl/DeviceItemImpl", node.Metadata!["OriginalQualifiedPaths"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.Summary.InputObjects);
        Assert.Equal(1, result.Summary.RemovedDuplicates);
        Assert.Equal(1, result.Summary.UniqueObjects);
    }

    [Fact]
    public void Deduplicate_PrefersRicherContent_WhenExtractionPriorityIsEqual()
    {
        var input = new[]
        {
            new TiaProjectObjectNode(
                "OB",
                "Main",
                "Project/BlockGroup/Blocks/Main",
                2,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ExtractionStrategy"] = "HostPlcModel"
                }),
            new TiaProjectObjectNode(
                "OB",
                "Main",
                "Project/BlockGroup/Main",
                2,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ExtractionStrategy"] = "HostPlcModel",
                    ["Content.ExportXml"] = "<OB />"
                })
        };

        var result = TiaInventoryDeduplicator.Deduplicate(input);

        Assert.Single(result.Objects);
        var selected = result.Objects[0];
        Assert.Equal("<OB />", selected.Metadata!["Content.ExportXml"]);
    }
}

