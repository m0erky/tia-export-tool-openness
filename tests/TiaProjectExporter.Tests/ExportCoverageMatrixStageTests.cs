using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class ExportCoverageMatrixStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesCoverageArtifacts()
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
                    ["ExtractionConfidence"] = "0.92"
                }),
                new TiaProjectObjectNode("Tag", "Tag_Start", "Project/PLC/Tags/Tag_Start", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "PLC.Tags",
                    ["ExtractionConfidence"] = "0.55"
                })
            ],
            Issues: new[]
            {
                new ExportIssue("PLC.Tags", "Tag table partially unavailable")
            }));

        var stage = new ExportCoverageMatrixStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var jsonArtifact = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/EXPORT_COVERAGE_MATRIX.json");
        var markdownArtifact = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/EXPORT_COVERAGE_MATRIX.md");

        using var document = JsonDocument.Parse(jsonArtifact.Content);
        Assert.True(document.RootElement.TryGetProperty("domains", out var domains));
        Assert.True(domains.GetArrayLength() >= 10);
        Assert.Contains("PLC.Blocks", markdownArtifact.Content, StringComparison.Ordinal);
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
