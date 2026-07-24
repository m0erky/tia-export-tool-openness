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
        context.AddIssue(new ExportIssue("Inventory", "No project path configured"));

        var stage = new ExportReportStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var overview = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/PROJECT_OVERVIEW.md");
        var report = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/EXPORT_REPORT.md");
        var statistics = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/PROJECT_STATISTICS.json");

        Assert.Contains("Recoverable issues", overview.Content, StringComparison.Ordinal);
        Assert.Contains("Inventory", report.Content, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(statistics.Content);
        var totalsElement = document.RootElement.GetProperty("totals");
        Assert.Equal(1, totalsElement.GetProperty("issues").GetInt32());
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
