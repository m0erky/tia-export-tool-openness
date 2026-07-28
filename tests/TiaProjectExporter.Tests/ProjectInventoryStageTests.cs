using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class ProjectInventoryStageTests
{
    [Fact]
    public async Task ExecuteAsync_ForwardsIncludedDomains_ToInventoryProvider()
    {
        var writer = new RecordingArtifactWriter();
        IReadOnlyCollection<ExportDomain>? capturedDomains = null;

        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out") with { IncludedDomains = new[] { ExportDomain.Blocks } },
            writer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var stage = new ProjectInventoryStage(new CapturingInventoryProvider(
            new TiaProjectInventory(
                TiaInventoryStatus.Complete,
                ProjectName: "Sample",
                ProjectPath: "C:/Sample.ap20",
                Objects: new[]
                {
                    new TiaProjectObjectNode("Project", "Sample", "Project", 0)
                },
                Issues: Array.Empty<ExportIssue>()),
            domains => capturedDomains = domains));

        await stage.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(capturedDomains);
        Assert.Single(capturedDomains!);
        Assert.Contains(ExportDomain.Blocks, capturedDomains!);
    }

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
        Assert.Equal(1, report.FailedCount);
    }

    private sealed class StubInventoryProvider : ITiaProjectInventoryProvider
    {
        private readonly TiaProjectInventory _inventory;

        public StubInventoryProvider(TiaProjectInventory inventory)
        {
            _inventory = inventory;
        }

        public Task<TiaProjectInventory> BuildInventoryAsync(
            string? projectPath,
            string? tiaInstallationPathOverride,
            CancellationToken cancellationToken,
            IReadOnlyCollection<ExportDomain>? includedDomains = null) =>
            Task.FromResult(_inventory);

        public Task<TiaProjectInventory> BuildInventoryPreviewAsync(
            string? projectPath,
            string? tiaInstallationPathOverride,
            CancellationToken cancellationToken,
            IReadOnlyCollection<ExportDomain>? includedDomains = null) =>
            Task.FromResult(_inventory);
    }

    private sealed class CapturingInventoryProvider : ITiaProjectInventoryProvider
    {
        private readonly TiaProjectInventory _inventory;
        private readonly Action<IReadOnlyCollection<ExportDomain>?> _capture;

        public CapturingInventoryProvider(TiaProjectInventory inventory, Action<IReadOnlyCollection<ExportDomain>?> capture)
        {
            _inventory = inventory;
            _capture = capture;
        }

        public Task<TiaProjectInventory> BuildInventoryAsync(
            string? projectPath,
            string? tiaInstallationPathOverride,
            CancellationToken cancellationToken,
            IReadOnlyCollection<ExportDomain>? includedDomains = null)
        {
            _capture(includedDomains);
            return Task.FromResult(_inventory);
        }

        public Task<TiaProjectInventory> BuildInventoryPreviewAsync(
            string? projectPath,
            string? tiaInstallationPathOverride,
            CancellationToken cancellationToken,
            IReadOnlyCollection<ExportDomain>? includedDomains = null) =>
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
