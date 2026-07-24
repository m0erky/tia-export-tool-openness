using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class ExportReadinessStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesReadinessArtifactsWithPriorities()
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
                    ["ExtractionConfidence"] = "0.90",
                    ["ExtractedByTypedExtractor"] = "true",
                    ["FallbackReflectionUsed"] = "false",
                    ["Calls"] = "FB_Main, MissingBlock"
                }),
                new TiaProjectObjectNode("FB", "FB_Main", "Project/PLC/Blocks/FB_Main", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "PLC.Blocks",
                    ["ExtractionConfidence"] = "0.85",
                    ["ExtractedByTypedExtractor"] = "true",
                    ["FallbackReflectionUsed"] = "false"
                }),
                new TiaProjectObjectNode("UnmappedHmiNode", "ScreenContainer", "Project/HMI/Screens/Container", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "HMI",
                    ["ExtractionConfidence"] = "0.55",
                    ["ExtractedByTypedExtractor"] = "false",
                    ["FallbackReflectionUsed"] = "true",
                    ["References"] = "Tag_Start"
                }),
                new TiaProjectObjectNode("Tag", "Tag_Start", "Project/PLC/Tags/Tag_Start", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "PLC.Tags",
                    ["ExtractionConfidence"] = "0.82",
                    ["ExtractedByTypedExtractor"] = "true",
                    ["FallbackReflectionUsed"] = "false"
                })
            ],
            Issues:
            [
                new ExportIssue("HMI", "Fallback-heavy extraction for HMI domain")
            ]));

        var stage = new ExportReadinessStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var markdown = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/EXPORT_READINESS_SCORE.md");
        var json = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/EXPORT_READINESS_SCORE.json");

        Assert.Contains("Overall readiness score", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("Priority Actions", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("HMI", markdown.Content, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json.Content);
        Assert.Equal("Partial", document.RootElement.GetProperty("status").GetString());
        Assert.True(document.RootElement.GetProperty("overallScore").GetInt32() >= 0);

        var domains = document.RootElement.GetProperty("domains").EnumerateArray().ToArray();
        Assert.Contains(domains, domain => domain.GetProperty("domain").GetString() == "PLC.Blocks");
        Assert.Contains(domains, domain => domain.GetProperty("domain").GetString() == "HMI");

        var priorityActions = document.RootElement.GetProperty("priorityActions").EnumerateArray().ToArray();
        Assert.NotEmpty(priorityActions);
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
