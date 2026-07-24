using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class DomainExtractorCoverageStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesCoverageAndGapArtifacts()
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
                    ["TypedExtractor"] = "PlcBlockDomainExtractor",
                    ["FallbackReflectionUsed"] = "false"
                }),
                new TiaProjectObjectNode("UnmappedHmiNode", "ScreenContainer", "Project/HMI/Screens/Container", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "HMI",
                    ["RuntimeType"] = "Siemens.Engineering.Hmi.ScreenContainer",
                    ["FallbackReflectionUsed"] = "true"
                })
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new DomainExtractorCoverageStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var markdown = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/DOMAIN_EXTRACTOR_COVERAGE.md");
        var json = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/DOMAIN_EXTRACTOR_COVERAGE.json");

        Assert.Contains("Coverage Matrix", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("Extractor Gaps", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("PlcBlockDomainExtractor", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("ScreenContainer", markdown.Content, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json.Content);
        var summary = document.RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("matrixRows").GetInt32() >= 2);
        Assert.True(summary.GetProperty("gapCount").GetInt32() >= 1);

        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().ToArray();
        Assert.Contains(gaps, gap => gap.GetProperty("runtimeType").GetString() == "Siemens.Engineering.Hmi.ScreenContainer");
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
