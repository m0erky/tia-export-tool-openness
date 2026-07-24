using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class ObjectUsageAnalysisStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesTagUsageAndUnusedObjectArtifacts()
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
                new TiaProjectObjectNode("FB", "FB_Main", "Project/PLC/Blocks/FB_Main", 2, new Dictionary<string, string>
                {
                    ["Calls"] = "FC_Helper",
                    ["Uses"] = "Tag_Start"
                }),
                new TiaProjectObjectNode("FC", "FC_Helper", "Project/PLC/Blocks/FC_Helper", 2),
                new TiaProjectObjectNode("FC", "FC_Unused", "Project/PLC/Blocks/FC_Unused", 2),
                new TiaProjectObjectNode("Tag", "Tag_Start", "Project/Tags/Default/Tag_Start", 2),
                new TiaProjectObjectNode("Tag", "Tag_Unused", "Project/Tags/Default/Tag_Unused", 2)
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new ObjectUsageAnalysisStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var tagUsageJson = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/TAG_USAGE.json");
        var unusedJson = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/UNUSED_OBJECTS.json");
        Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/TAG_USAGE.md");
        Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/UNUSED_OBJECTS.md");

        using var tagDocument = JsonDocument.Parse(tagUsageJson.Content);
        Assert.Equal(2, tagDocument.RootElement.GetProperty("totalTags").GetInt32());

        using var unusedDocument = JsonDocument.Parse(unusedJson.Content);
        Assert.True(unusedDocument.RootElement.GetProperty("unusedCount").GetInt32() >= 1);
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
