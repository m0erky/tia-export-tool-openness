using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class TypedExtractorBacklogStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesBacklogArtifacts()
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
                    ["Domain"] = "PLC.Blocks",
                    ["RuntimeType"] = "Siemens.Engineering.SW.Blocks.FBBlock",
                    ["FallbackReflectionUsed"] = "false",
                    ["ExtractedByTypedExtractor"] = "true",
                    ["TypedExtractor"] = "PlcBlockDomainExtractor",
                    ["ExtractionConfidence"] = "0.92",
                    ["Calls"] = "FC_Helper, MissingBlock"
                }),
                new TiaProjectObjectNode("UnmappedHmiNode", "ScreenContainer", "Project/HMI/Screens/Container", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "HMI",
                    ["RuntimeType"] = "Siemens.Engineering.Hmi.ScreenContainer",
                    ["FallbackReflectionUsed"] = "true",
                    ["ExtractedByTypedExtractor"] = "false",
                    ["ExtractionConfidence"] = "0.55",
                    ["References"] = "MissingTag"
                }),
                new TiaProjectObjectNode("Tag", "Tag_Start", "Project/PLC/Tags/Tag_Start", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "PLC.Tags",
                    ["RuntimeType"] = "Siemens.Engineering.SW.Tags.PlcTag",
                    ["FallbackReflectionUsed"] = "false",
                    ["ExtractedByTypedExtractor"] = "true",
                    ["TypedExtractor"] = "PlcTagDomainExtractor",
                    ["ExtractionConfidence"] = "0.81"
                })
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new TypedExtractorBacklogStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var markdown = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/TYPED_EXTRACTOR_BACKLOG.md");
        var json = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/TYPED_EXTRACTOR_BACKLOG.json");

        Assert.Contains("Top Priorities", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("ScreenContainer", markdown.Content, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json.Content);
        var summary = document.RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("prioritizedEntries").GetInt32() > 0);

        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Contains(entries, entry => entry.GetProperty("runtimeType").GetString() == "Siemens.Engineering.Hmi.ScreenContainer");
        Assert.Contains(entries, entry => entry.GetProperty("impactScore").GetInt32() > 0);
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
