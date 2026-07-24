using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Tia.Discovery;

namespace TiaProjectExporter.Tests;

public sealed class RegistryTiaInstallationDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_ReturnsEmpty_WhenHostIsNotWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new RegistryTiaInstallationDiscoveryService(NullLogger<RegistryTiaInstallationDiscoveryService>.Instance);

        var results = await service.DiscoverAsync(CancellationToken.None);

        Assert.Empty(results);
    }
}
