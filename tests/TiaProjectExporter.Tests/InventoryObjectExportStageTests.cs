using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class InventoryObjectExportStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesPerObjectArtifactsIntoDomainFolders()
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
                    ObjectType: "HmiObject",
                    Name: "MainScreen",
                    QualifiedPath: "Project/HMI/Screens/MainScreen",
                    Depth: 2,
                    Metadata: new Dictionary<string, string>())
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new InventoryObjectExportStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath.StartsWith("Export/Hardware/Objects/", StringComparison.Ordinal) && artifact.RelativePath.EndsWith(".json", StringComparison.Ordinal));
        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath.StartsWith("Export/HMI/Objects/", StringComparison.Ordinal) && artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal));

        var result = Assert.Single(context.Results, item => item.ObjectType == "InventoryObjects");
        Assert.Equal(ExportObjectStatus.Succeeded, result.Status);
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
