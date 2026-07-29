using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class BlockCallGraphStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesGraphFromInventoryBlocks()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            NullLogger.Instance);

        context.SetInventory(new TiaProjectInventory(
            TiaInventoryStatus.Partial,
            ProjectName: "Demo",
            ProjectPath: "C:/Projects/Demo.ap19",
            Objects:
            [
                new TiaProjectObjectNode("OB", "OB1", "Project/PLC/Blocks/OB1", 2, new Dictionary<string, string> { ["Calls"] = "FB_Main" }),
                new TiaProjectObjectNode("FB", "FB_Main", "Project/PLC/Blocks/FB_Main", 2, new Dictionary<string, string> { ["Calls"] = "FC_Helper; DB_Config; ExternalBlock" }),
                new TiaProjectObjectNode("FC", "FC_Helper", "Project/PLC/Blocks/FC_Helper", 2),
                new TiaProjectObjectNode("DB", "DB_Config", "Project/PLC/Blocks/DB_Config", 2)
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new BlockCallGraphStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var graphArtifact = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/BLOCK_CALL_GRAPH.md");
        Assert.Contains("mermaid", graphArtifact.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Summary", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("Entry Points", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("Unresolved Targets", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("-.->", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("OB1", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("FB_Main", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("FC_Helper", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("ExternalBlock", graphArtifact.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsCallsFromObExportXml_WhenMetadataCallsMissing()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            NullLogger.Instance);

        context.SetInventory(new TiaProjectInventory(
            TiaInventoryStatus.Partial,
            ProjectName: "Demo",
            ProjectPath: "C:/Projects/Demo.ap19",
            Objects:
            [
                new TiaProjectObjectNode("OB", "Main", "Project/PLC/Blocks/Main", 2, new Dictionary<string, string>
                {
                    ["Content.ExportXml"] = """
                    <Document>
                      <CallInfo Name="Block_1" BlockType="FB"><Component Name="Block_1_DB" /></CallInfo>
                      <CallInfo Name="Block_2" BlockType="FB"><Component Name="Block_2_DB" /></CallInfo>
                    </Document>
                    """
                }),
                new TiaProjectObjectNode("FB", "Block_1", "Project/PLC/Blocks/Block_1", 2),
                new TiaProjectObjectNode("FB", "Block_2", "Project/PLC/Blocks/Block_2", 2)
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new BlockCallGraphStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var graphArtifact = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/BLOCK_CALL_GRAPH.md");
        Assert.Contains("Call edges: **2**", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("Main", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("Block_1", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("Block_2", graphArtifact.Content, StringComparison.Ordinal);
    }

    private sealed class RecordingArtifactWriter : IExportArtifactWriter
    {
        public List<ExportArtifact> Artifacts { get; } = [];

        public Task EnsureDirectoryAsync(string relativePath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteArtifactAsync(ExportArtifact artifact, CancellationToken cancellationToken)
        {
            Artifacts.Add(artifact);
            return Task.CompletedTask;
        }
    }
}
