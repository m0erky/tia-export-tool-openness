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
                new TiaProjectObjectNode("FB", "FB_Main", "Project/PLC/Blocks/FB_Main", 2, new Dictionary<string, string> { ["Calls"] = "FC_Helper, DB_Config" }),
                new TiaProjectObjectNode("FC", "FC_Helper", "Project/PLC/Blocks/FC_Helper", 2),
                new TiaProjectObjectNode("DB", "DB_Config", "Project/PLC/Blocks/DB_Config", 2)
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new BlockCallGraphStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var graphArtifact = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/BLOCK_CALL_GRAPH.md");
        Assert.Contains("mermaid", graphArtifact.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FB_Main", graphArtifact.Content, StringComparison.Ordinal);
        Assert.Contains("FC_Helper", graphArtifact.Content, StringComparison.Ordinal);
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
