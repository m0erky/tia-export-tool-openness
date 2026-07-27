using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class InventoryObjectExportStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesDomainTypeBundlesWithDeepContent()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        context.SetInventory(new TiaProjectInventory(
            TiaInventoryStatus.Complete,
            ProjectName: "Sample",
            ProjectPath: "sample.ap20",
            Objects:
            [
                new TiaProjectObjectNode(
                    ObjectType: "Device",
                    Name: "PLC_1",
                    QualifiedPath: "Project/Devices/PLC_1",
                    Depth: 1,
                    Metadata: new Dictionary<string, string> { ["RuntimeType"] = "DeviceType" }),
                new TiaProjectObjectNode(
                    ObjectType: "FB",
                    Name: "FB100",
                    QualifiedPath: "Project/Devices/PLC_1/Software/Blocks/FB100",
                    Depth: 2,
                    Metadata: new Dictionary<string, string>
                    {
                        ["Content.ExportXml"] = "<FB />",
                        ["Content.SourceText"] = "FUNCTION_BLOCK FB100"
                    })
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new InventoryObjectExportStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var blocksJson = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Blocks/Bundles/FB.json");
        Assert.Contains("FUNCTION_BLOCK FB100", blocksJson.Content, StringComparison.Ordinal);

        var blocksMarkdown = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Blocks/Bundles/FB.md");
        Assert.Contains("```text", blocksMarkdown.Content, StringComparison.Ordinal);
        Assert.Contains("```xml", blocksMarkdown.Content, StringComparison.Ordinal);

        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath == "Export/Hardware/Bundles/Device.json");

        var result = Assert.Single(context.Results, item => item.ObjectType == "InventoryObjects");
        Assert.Equal(ExportObjectStatus.Succeeded, result.Status);
        Assert.Contains("bundles", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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
