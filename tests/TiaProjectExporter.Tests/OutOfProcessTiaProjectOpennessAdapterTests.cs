using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Tia.Inventory;

namespace TiaProjectExporter.Tests;

public sealed class OutOfProcessTiaProjectOpennessAdapterTests
{
    [Fact]
    public async Task TraverseAsync_ReturnsIssue_OnNonWindowsHosts()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var adapter = new OutOfProcessTiaProjectOpennessAdapter(
            new StubDiscoveryService(Array.Empty<DiscoveredTiaPortalInstallation>()),
            NullLogger<OutOfProcessTiaProjectOpennessAdapter>.Instance);

        var result = await adapter.TraverseAsync("/tmp/sample.ap20", null, TiaTraversalDetailLevel.Full, CancellationToken.None);

        Assert.NotEmpty(result.Objects);
        Assert.Contains(result.Issues, issue => issue.Scope == "OpennessHost");
    }

    private sealed class StubDiscoveryService : ITiaInstallationDiscoveryService
    {
        private readonly IReadOnlyList<DiscoveredTiaPortalInstallation> _results;

        public StubDiscoveryService(IReadOnlyList<DiscoveredTiaPortalInstallation> results)
        {
            _results = results;
        }

        public Task<IReadOnlyList<DiscoveredTiaPortalInstallation>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_results);
    }
}
