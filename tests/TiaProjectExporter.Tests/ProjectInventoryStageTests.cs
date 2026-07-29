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
    public async Task ExecuteAsync_ForwardsSafetyOfflineProgramPassword_ToInventoryProvider()
    {
        var writer = new RecordingArtifactWriter();
        string? capturedPassword = null;

        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out") with { SafetyOfflineProgramPassword = "safety-secret" },
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
            _ => { },
            safetyPassword => capturedPassword = safetyPassword));

        await stage.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("safety-secret", capturedPassword);
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
        Assert.Equal(2, report.Issues.Count);
        Assert.Contains(report.Issues, issue => issue.Scope == "InventoryDeduplication");
        Assert.Equal(1, report.FailedCount);
    }

    [Fact]
    public async Task ExecuteAsync_DeduplicatesInventoryBeforeStoringInContext()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var inventory = new TiaProjectInventory(
            TiaInventoryStatus.Complete,
            ProjectName: "Sample",
            ProjectPath: "C:/Sample.ap20",
            Objects: new[]
            {
                new TiaProjectObjectNode("OB", "Main", "Project/DeviceItemImpl/DeviceItemImpl/BlockGroup/Blocks/Main", 2,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ExtractionStrategy"] = "HostReflection"
                    }),
                new TiaProjectObjectNode("OB", "Main", "Project/DeviceItemImpl/BlockGroup/Main", 2,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ExtractedByTypedExtractor"] = "true"
                    })
            },
            Issues: Array.Empty<ExportIssue>());

        var stage = new ProjectInventoryStage(new StubInventoryProvider(inventory));

        await stage.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(context.Inventory);
        Assert.Single(context.Inventory!.Objects);
        Assert.Equal("Project/DeviceItemImpl/BlockGroup/Main", context.Inventory.Objects[0].QualifiedPath);
        Assert.NotNull(context.Inventory.DeduplicationSummary);
        Assert.Equal(1, context.Inventory.DeduplicationSummary!.RemovedDuplicates);
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
            IReadOnlyCollection<ExportDomain>? includedDomains = null,
            string? safetyOfflineProgramPassword = null) =>
            Task.FromResult(_inventory);

        public Task<TiaProjectInventory> BuildInventoryPreviewAsync(
            string? projectPath,
            string? tiaInstallationPathOverride,
            CancellationToken cancellationToken,
            IReadOnlyCollection<ExportDomain>? includedDomains = null,
            string? safetyOfflineProgramPassword = null) =>
            Task.FromResult(_inventory);
    }

    private sealed class CapturingInventoryProvider : ITiaProjectInventoryProvider
    {
        private readonly TiaProjectInventory _inventory;
        private readonly Action<IReadOnlyCollection<ExportDomain>?> _capture;
        private readonly Action<string?> _captureSafetyPassword;

        public CapturingInventoryProvider(
            TiaProjectInventory inventory,
            Action<IReadOnlyCollection<ExportDomain>?> capture,
            Action<string?>? captureSafetyPassword = null)
        {
            _inventory = inventory;
            _capture = capture;
            _captureSafetyPassword = captureSafetyPassword ?? (_ => { });
        }

        public Task<TiaProjectInventory> BuildInventoryAsync(
            string? projectPath,
            string? tiaInstallationPathOverride,
            CancellationToken cancellationToken,
            IReadOnlyCollection<ExportDomain>? includedDomains = null,
            string? safetyOfflineProgramPassword = null)
        {
            _capture(includedDomains);
            _captureSafetyPassword(safetyOfflineProgramPassword);
            return Task.FromResult(_inventory);
        }

        public Task<TiaProjectInventory> BuildInventoryPreviewAsync(
            string? projectPath,
            string? tiaInstallationPathOverride,
            CancellationToken cancellationToken,
            IReadOnlyCollection<ExportDomain>? includedDomains = null,
            string? safetyOfflineProgramPassword = null) =>
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
