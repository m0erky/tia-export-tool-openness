using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class ExportReportStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesExecutionDrivenArtifacts()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            NullLogger.Instance);

        context.AddResult(new ExportedObjectResult("Stage", "Inventory", ExportObjectStatus.Skipped, "Unavailable"));
        context.AddResult(new ExportedObjectResult("Packaging", "ExportZip", ExportObjectStatus.Succeeded, "/tmp/out/Export.zip"));
        context.SetArchiveInfo(new ExportArchiveInfo("/tmp/out/Export.zip", 1024, "ABCDEF", DateTimeOffset.UtcNow));
        context.SetInventory(
            new TiaProjectInventory(
                TiaInventoryStatus.Partial,
                "DemoProject",
                "/tmp/demo.ap18",
                new[]
                {
                    new TiaProjectObjectNode(
                        "UnmappedRuntimeNode",
                        "UnknownContainer",
                        "Project/Unknown/UnknownContainer",
                        2,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Domain"] = "Unmapped",
                            ["RuntimeType"] = "Siemens.Engineering.UnknownContainer",
                            ["FallbackReflectionUsed"] = "true",
                            ["ExtractedByTypedExtractor"] = "false"
                        }),
                    new TiaProjectObjectNode(
                        "Module",
                        "DI_16x24VDC",
                        "Project/Hardware/Rack_1/DI_16x24VDC",
                        3,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Domain"] = "Hardware",
                            ["RuntimeType"] = "Siemens.Engineering.HW.Module",
                            ["FallbackReflectionUsed"] = "false",
                            ["ExtractedByTypedExtractor"] = "true"
                        })
                },
                Array.Empty<ExportIssue>(),
                new InventoryDeduplicationSummary(
                    InputObjects: 5,
                    RemovedDuplicates: 2,
                    UniqueObjects: 3,
                    TopDuplicateGroups: new[]
                    {
                        new InventoryDuplicateGroup("OB", "Project/BlockGroup/Main", 3)
                    })));
        context.AddIssue(new ExportIssue("Inventory", "No project path configured"));

        var stage = new ExportReportStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var overview = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/PROJECT_OVERVIEW.md");
        var report = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/EXPORT_REPORT.md");
        var statistics = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/PROJECT_STATISTICS.json");

        Assert.Contains("Recoverable issues", overview.Content, StringComparison.Ordinal);
        Assert.Contains("Analysis Hub", overview.Content, StringComparison.Ordinal);
        Assert.Contains("Reflection Fallback Hotspots", overview.Content, StringComparison.Ordinal);
        Assert.Contains("Packaging", report.Content, StringComparison.Ordinal);
        Assert.Contains("Archive SHA-256", report.Content, StringComparison.Ordinal);
        Assert.Contains("Inventory", report.Content, StringComparison.Ordinal);
        Assert.Contains("Deduplication Summary", report.Content, StringComparison.Ordinal);
        Assert.Contains("Removed duplicates", report.Content, StringComparison.Ordinal);
        Assert.Contains("Reflection fallback objects", report.Content, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(statistics.Content);
        var totalsElement = document.RootElement.GetProperty("totals");
        Assert.Equal(1, totalsElement.GetProperty("issues").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("archive", out _));
        Assert.Equal(2, document.RootElement.GetProperty("deduplication").GetProperty("removedDuplicates").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("fallbackExtraction").GetProperty("totalFallbackObjects").GetInt32());
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
