using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class DependencyGraphStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesDependenciesFromInventoryMetadata()
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
                    ["Calls"] = "FC_Helper, Project\\PLC\\Blocks\\DB_Config",
                    ["Uses"] = "UDT_Motor",
                    ["TagUsage"] = "Tag_Start, Tag_Stop"
                }),
                new TiaProjectObjectNode("FC", "FC_Helper", "Project/PLC/Blocks/FC_Helper", 2),
                new TiaProjectObjectNode("DB", "DB_Config", "Project/PLC/Blocks/DB_Config", 2),
                new TiaProjectObjectNode("Tag", "Tag_Start", "Project/PLC/Tags/Tag_Start", 2)
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new DependencyGraphStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var artifact = Assert.Single(writer.Artifacts, entry => entry.RelativePath == "Export/DEPENDENCIES.json");
        using var json = JsonDocument.Parse(artifact.Content);

        Assert.Equal("Partial", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("summary").GetProperty("edgeCount").GetInt32() >= 5);
        Assert.True(json.RootElement.GetProperty("summary").GetProperty("resolvedEdges").GetInt32() >= 3);
        Assert.True(json.RootElement.GetProperty("summary").GetProperty("unresolvedEdges").GetInt32() >= 1);

        var edges = json.RootElement.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Contains(edges, edge => edge.GetProperty("relationship").GetString() == "Calls");
        Assert.Contains(edges, edge => edge.GetProperty("relationship").GetString() == "UsesTag");

        var unresolvedTargets = json.RootElement.GetProperty("summary").GetProperty("topUnresolvedTargets").EnumerateArray().ToArray();
        Assert.Contains(unresolvedTargets, target => target.GetProperty("target").GetString() == "UDT_Motor");
    }

    [Fact]
    public async Task ExecuteAsync_MapsInstanceDbTargetsToInstanceOfName()
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
                    ["Calls"] = "Block_1_DB"
                }),
                new TiaProjectObjectNode("InstanceDB", "Block_1_DB", "Project/PLC/Blocks/Block_1_DB", 2, new Dictionary<string, string>
                {
                    ["InstanceOfName"] = "Block_1"
                }),
                new TiaProjectObjectNode("FB", "Block_1", "Project/PLC/Blocks/Block_1", 2)
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new DependencyGraphStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var artifact = Assert.Single(writer.Artifacts, entry => entry.RelativePath == "Export/DEPENDENCIES.json");
        using var json = JsonDocument.Parse(artifact.Content);
        var edges = json.RootElement.GetProperty("edges").EnumerateArray().ToArray();

        Assert.Contains(edges, edge =>
            edge.GetProperty("sourceName").GetString() == "Main"
            && edge.GetProperty("relationship").GetString() == "Calls"
            && edge.GetProperty("target").GetString() == "Block_1");
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
