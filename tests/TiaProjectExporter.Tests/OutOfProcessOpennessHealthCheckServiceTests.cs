using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Tia.Inventory;

namespace TiaProjectExporter.Tests;

public sealed class OutOfProcessOpennessHealthCheckServiceTests
{
    [Fact]
    public async Task CheckAsync_ReturnsUnhealthy_OnNonWindowsHosts()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new OutOfProcessOpennessHealthCheckService(
            new StubDiscoveryService(Array.Empty<DiscoveredTiaPortalInstallation>()),
            NullLogger<OutOfProcessOpennessHealthCheckService>.Instance);

        var result = await service.CheckAsync(null, CancellationToken.None);

        Assert.Equal(OpennessHealthCheckState.Unhealthy, result.State);
        Assert.Contains(result.Details, detail => detail.Contains("Windows", StringComparison.OrdinalIgnoreCase));
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
