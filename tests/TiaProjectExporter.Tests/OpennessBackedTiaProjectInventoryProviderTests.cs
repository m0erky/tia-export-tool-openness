using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Tia.Inventory;

namespace TiaProjectExporter.Tests;

public sealed class OpennessBackedTiaProjectInventoryProviderTests
{
    [Fact]
    public async Task BuildInventoryPreviewAsync_ForwardsPreviewDetailLevel_ToAdapter()
    {
        TiaTraversalDetailLevel? capturedDetailLevel = null;

        var provider = new OpennessBackedTiaProjectInventoryProvider(new StubOpennessAdapter((projectPath, _, detailLevel) =>
        {
            capturedDetailLevel = detailLevel;

            return Task.FromResult(
                new TiaProjectTraversalResult(
                    ProjectName: "Sample",
                    ProjectPath: projectPath,
                    Objects: new[]
                    {
                        new TiaProjectObjectNode("Project", "Sample", "Project", 0)
                    },
                    Issues: Array.Empty<ExportIssue>()));
        }));

        _ = await provider.BuildInventoryPreviewAsync("C:/Projects/Sample.ap20", null, CancellationToken.None);

        Assert.Equal(TiaTraversalDetailLevel.Preview, capturedDetailLevel);
    }

    [Fact]
    public async Task BuildInventoryAsync_ForwardsFullDetailLevel_ToAdapter()
    {
        TiaTraversalDetailLevel? capturedDetailLevel = null;

        var provider = new OpennessBackedTiaProjectInventoryProvider(new StubOpennessAdapter((projectPath, _, detailLevel) =>
        {
            capturedDetailLevel = detailLevel;

            return Task.FromResult(
                new TiaProjectTraversalResult(
                    ProjectName: "Sample",
                    ProjectPath: projectPath,
                    Objects: new[]
                    {
                        new TiaProjectObjectNode("Project", "Sample", "Project", 0)
                    },
                    Issues: Array.Empty<ExportIssue>()));
        }));

        _ = await provider.BuildInventoryAsync("C:/Projects/Sample.ap20", null, CancellationToken.None);

        Assert.Equal(TiaTraversalDetailLevel.Full, capturedDetailLevel);
    }

    [Fact]
    public async Task BuildInventoryAsync_ReturnsUnavailable_WhenProjectPathMissing()
    {
        var provider = new OpennessBackedTiaProjectInventoryProvider(new StubOpennessAdapter((_, _, _) => throw new InvalidOperationException()));

        var inventory = await provider.BuildInventoryAsync(null, null, CancellationToken.None);

        Assert.Equal(TiaInventoryStatus.Unavailable, inventory.Status);
        Assert.Empty(inventory.Objects);
        Assert.Single(inventory.Issues);
    }

    [Fact]
    public async Task BuildInventoryAsync_ReturnsPartial_WhenTraversalReturnsIssues()
    {
        var provider = new OpennessBackedTiaProjectInventoryProvider(new StubOpennessAdapter((projectPath, _, _) =>
            Task.FromResult(
                new TiaProjectTraversalResult(
                    ProjectName: "Sample",
                    ProjectPath: projectPath,
                    Objects: new[]
                    {
                        new TiaProjectObjectNode("Project", "Sample", "Project", 0),
                        new TiaProjectObjectNode("Device", "PLC_1", "Project/Devices/PLC_1", 1)
                    },
                    Issues:
                    [
                        new ExportIssue("DeviceTraversal", "One device could not be read")
                    ]))));

        var inventory = await provider.BuildInventoryAsync("C:/Projects/Sample.ap18", null, CancellationToken.None);

        Assert.Equal(TiaInventoryStatus.Partial, inventory.Status);
        Assert.Equal(2, inventory.Objects.Count);
        Assert.Single(inventory.Issues);
    }

    [Fact]
    public async Task BuildInventoryAsync_ReturnsUnavailable_WhenOnlyRootObjectExistsAndIssuesPresent()
    {
        var provider = new OpennessBackedTiaProjectInventoryProvider(new StubOpennessAdapter((projectPath, _, _) =>
            Task.FromResult(
                new TiaProjectTraversalResult(
                    ProjectName: "Sample",
                    ProjectPath: projectPath,
                    Objects: new[]
                    {
                        new TiaProjectObjectNode("Project", "Sample", "Project", 0)
                    },
                    Issues:
                    [
                        new ExportIssue("OpennessTraversal", "Traversal failed")
                    ]))));

        var inventory = await provider.BuildInventoryAsync("C:/Projects/Sample.ap20", null, CancellationToken.None);

        Assert.Equal(TiaInventoryStatus.Unavailable, inventory.Status);
        Assert.Single(inventory.Objects);
        Assert.Single(inventory.Issues);
    }

    [Fact]
    public async Task BuildInventoryAsync_ReturnsUnavailable_WhenTraversalThrows()
    {
        var provider = new OpennessBackedTiaProjectInventoryProvider(new StubOpennessAdapter((_, _, _) => throw new InvalidOperationException("Boom")));

        var inventory = await provider.BuildInventoryAsync("C:/Projects/Sample.ap18", null, CancellationToken.None);

        Assert.Equal(TiaInventoryStatus.Unavailable, inventory.Status);
        Assert.Single(inventory.Issues);
        Assert.Equal("OpennessTraversal", inventory.Issues[0].Scope);
    }

    [Fact]
    public async Task BuildInventoryAsync_ForwardsInstallationPathOverride_ToAdapter()
    {
        string? capturedOverride = null;

        var provider = new OpennessBackedTiaProjectInventoryProvider(new StubOpennessAdapter((projectPath, overridePath, _) =>
        {
            capturedOverride = overridePath;

            return Task.FromResult(
                new TiaProjectTraversalResult(
                    ProjectName: "Sample",
                    ProjectPath: projectPath,
                    Objects: Array.Empty<TiaProjectObjectNode>(),
                    Issues: Array.Empty<ExportIssue>()));
        }));

        _ = await provider.BuildInventoryAsync("C:/Projects/Sample.ap20", "C:/Program Files/Siemens/Automation/Portal V20", CancellationToken.None);

        Assert.Equal("C:/Program Files/Siemens/Automation/Portal V20", capturedOverride);
    }

    private sealed class StubOpennessAdapter : ITiaProjectOpennessAdapter
    {
        private readonly Func<string, string?, TiaTraversalDetailLevel, Task<TiaProjectTraversalResult>> _traverseAsync;

        public StubOpennessAdapter(Func<string, string?, TiaTraversalDetailLevel, Task<TiaProjectTraversalResult>> traverseAsync)
        {
            _traverseAsync = traverseAsync;
        }

        public Task<TiaProjectTraversalResult> TraverseAsync(string projectPath, string? tiaInstallationPathOverride, TiaTraversalDetailLevel detailLevel, CancellationToken cancellationToken) =>
            _traverseAsync(projectPath, tiaInstallationPathOverride, detailLevel);
    }
}
