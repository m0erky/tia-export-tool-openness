using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class ProjectInventoryStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesInventoryArtifactsAndIssues()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var stage = new ProjectInventoryStage(new StubInventoryProvider(
            new TiaProjectInventory(
                TiaInventoryStatus.Unavailable,
                ProjectName: null,
                ProjectPath: null,
                Objects: Array.Empty<TiaProjectObjectNode>(),
                Issues: new[] { new ExportIssue("Inventory", "Unavailable") })));

        await stage.ExecuteAsync(context, CancellationToken.None);

        var report = context.BuildReport();

        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath.EndsWith("TIA_PROJECT_INVENTORY.json", StringComparison.Ordinal));
        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath.EndsWith("TIA_PROJECT_INVENTORY.md", StringComparison.Ordinal));
        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath.EndsWith("AI_PROJECT_SUMMARY.md", StringComparison.Ordinal));
        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath.EndsWith("AI_HARDWARE_SUMMARY.md", StringComparison.Ordinal));
        Assert.Single(report.Issues);
    }

    private sealed class StubInventoryProvider : ITiaProjectInventoryProvider
    {
        private readonly TiaProjectInventory _inventory;

        public StubInventoryProvider(TiaProjectInventory inventory)
        {
            _inventory = inventory;
        }

        public Task<TiaProjectInventory> BuildInventoryAsync(string? projectPath, string? tiaInstallationPathOverride, CancellationToken cancellationToken) =>
            Task.FromResult(_inventory);
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
