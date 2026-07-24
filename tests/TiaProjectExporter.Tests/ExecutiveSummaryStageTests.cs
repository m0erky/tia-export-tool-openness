using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class ExecutiveSummaryStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesExecutiveSummaryArtifacts()
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
                    ["FallbackReflectionUsed"] = "false"
                }),
                new TiaProjectObjectNode("UnmappedHmiNode", "ScreenContainer", "Project/HMI/Screens/Container", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "HMI",
                    ["FallbackReflectionUsed"] = "true",
                    ["References"] = "MissingTag"
                })
            ],
            Issues: Array.Empty<ExportIssue>()));

        context.AddResult(new ExportedObjectResult("Stage", "Inventory", ExportObjectStatus.Succeeded));
        context.AddIssue(new ExportIssue("Inventory", "Test issue"));

        var stage = new ExecutiveSummaryStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var markdown = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/EXECUTIVE_SUMMARY.md");
        var json = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/EXECUTIVE_SUMMARY.json");

        Assert.Contains("Executive Summary", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("Priority Actions", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("fallback extraction", markdown.Content, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(json.Content);
        Assert.True(document.RootElement.GetProperty("totals").GetProperty("resultCount").GetInt32() >= 1);
        var priorities = document.RootElement.GetProperty("priorities").EnumerateArray().ToArray();
        Assert.NotEmpty(priorities);
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
