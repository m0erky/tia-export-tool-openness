using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class ExportIndexStageTests
{
    [Fact]
    public async Task ExecuteAsync_BuildsIndexesFromActualArtifacts()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            NullLogger.Instance);

        await context.EnsureDirectoryAsync("Export", CancellationToken.None);
        await context.EnsureDirectoryAsync("Export/Reports", CancellationToken.None);
        await context.WriteArtifactAsync(new ExportArtifact("Export/README.md", ExportFormat.Markdown, "# Readme"), CancellationToken.None);
        await context.WriteArtifactAsync(new ExportArtifact("Export/Reports/TIA_PROJECT_INVENTORY.json", ExportFormat.Json, "{}"), CancellationToken.None);

        var stage = new ExportIndexStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var fileIndex = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/FILE_INDEX.json");
        var searchIndex = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/SEARCH_INDEX.json");
        var projectTree = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/PROJECT_TREE.txt");

        using var fileIndexDoc = JsonDocument.Parse(fileIndex.Content);
        Assert.True(fileIndexDoc.RootElement.GetProperty("fileCount").GetInt32() >= 2);

        using var searchIndexDoc = JsonDocument.Parse(searchIndex.Content);
        Assert.True(searchIndexDoc.RootElement.GetProperty("tokenCount").GetInt32() > 0);

        Assert.Contains("Export/Reports/TIA_PROJECT_INVENTORY.json", projectTree.Content, StringComparison.Ordinal);
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
