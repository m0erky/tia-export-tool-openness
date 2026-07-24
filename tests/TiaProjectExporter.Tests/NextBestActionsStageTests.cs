using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class NextBestActionsStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesPrioritizedActionPlanArtifacts()
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
                    ["Domain"] = "PLC.Blocks",
                    ["ExtractedByTypedExtractor"] = "true",
                    ["FallbackReflectionUsed"] = "false",
                    ["ExtractionConfidence"] = "0.90",
                    ["Calls"] = "FB_Main, MissingBlock"
                }),
                new TiaProjectObjectNode("FB", "FB_Main", "Project/PLC/Blocks/FB_Main", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "PLC.Blocks",
                    ["ExtractedByTypedExtractor"] = "true",
                    ["FallbackReflectionUsed"] = "false",
                    ["ExtractionConfidence"] = "0.88",
                    ["TagUsage"] = "Tag_Start"
                }),
                new TiaProjectObjectNode("UnmappedHmiNode", "ScreenContainer", "Project/HMI/Screens/Container", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "HMI",
                    ["ExtractedByTypedExtractor"] = "false",
                    ["FallbackReflectionUsed"] = "true",
                    ["ExtractionConfidence"] = "0.52",
                    ["References"] = "Tag_Start, MissingTag"
                }),
                new TiaProjectObjectNode("Tag", "Tag_Start", "Project/PLC/Tags/Tag_Start", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "PLC.Tags",
                    ["ExtractedByTypedExtractor"] = "true",
                    ["FallbackReflectionUsed"] = "false",
                    ["ExtractionConfidence"] = "0.83"
                })
            ],
            Issues:
            [
                new ExportIssue("HMI", "HMI extraction warning")
            ]));

        var stage = new NextBestActionsStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var markdown = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/NEXT_BEST_ACTIONS.md");
        var json = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/NEXT_BEST_ACTIONS.json");

        Assert.Contains("Prioritized Actions", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("RelationshipResolution", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("FallbackReduction", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("HMI", markdown.Content, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json.Content);
        var summary = document.RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("totalActions").GetInt32() > 0);

        var actions = document.RootElement.GetProperty("actions").EnumerateArray().ToArray();
        Assert.Contains(actions, action => action.GetProperty("domain").GetString() == "HMI");
        Assert.Contains(actions, action => action.GetProperty("category").GetString() == "RelationshipResolution");
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
