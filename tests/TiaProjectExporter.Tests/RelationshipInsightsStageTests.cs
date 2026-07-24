using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class RelationshipInsightsStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesRelationshipInsightsArtifacts()
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
                new TiaProjectObjectNode("OB", "OB1", "Project/PLC/Blocks/OB1", 2, new Dictionary<string, string>
                {
                    ["Calls"] = "FB_Main",
                    ["TagUsage"] = "Tag_Start"
                }),
                new TiaProjectObjectNode("FB", "FB_Main", "Project/PLC/Blocks/FB_Main", 2, new Dictionary<string, string>
                {
                    ["Calls"] = "FC_Helper, ExternalBlock",
                    ["Uses"] = "UDT_Motor"
                }),
                new TiaProjectObjectNode("FC", "FC_Helper", "Project/PLC/Blocks/FC_Helper", 2),
                new TiaProjectObjectNode("Tag", "Tag_Start", "Project/PLC/Tags/Tag_Start", 2)
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new RelationshipInsightsStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var markdown = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/RELATIONSHIP_INSIGHTS.md");
        var json = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/RELATIONSHIP_INSIGHTS.json");

        Assert.Contains("Relationship Breakdown", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("Unresolved Hotspots", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("Guidance", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("ExternalBlock", markdown.Content, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json.Content);
        Assert.Equal("Partial", document.RootElement.GetProperty("status").GetString());
        Assert.True(document.RootElement.GetProperty("summary").GetProperty("edgeCount").GetInt32() >= 4);
        Assert.True(document.RootElement.GetProperty("summary").GetProperty("unresolvedEdges").GetInt32() >= 1);

        var relationships = document.RootElement.GetProperty("relationships").EnumerateArray().ToArray();
        Assert.Contains(relationships, entry => entry.GetProperty("name").GetString() == "Calls");
        Assert.Contains(relationships, entry => entry.GetProperty("name").GetString() == "UsesTag");
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
