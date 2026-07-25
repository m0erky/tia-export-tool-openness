using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Tia.Inventory;
using TiaProjectExporter.Tia.Inventory.Extraction;

namespace TiaProjectExporter.Tests;

public sealed class ReflectionTiaProjectOpennessAdapterTests
{
    [Fact]
    public async Task TraverseAsync_ReturnsIssue_OnNonWindowsHosts()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var adapter = new ReflectionTiaProjectOpennessAdapter(
            new StubDiscoveryService(Array.Empty<DiscoveredTiaPortalInstallation>()),
            Array.Empty<ITiaDomainExtractor>(),
            NullLogger<ReflectionTiaProjectOpennessAdapter>.Instance);

        var result = await adapter.TraverseAsync("/tmp/sample.ap18", null, CancellationToken.None);

        Assert.NotEmpty(result.Objects);
        Assert.Contains(result.Issues, issue => issue.Scope == "OpennessRuntime");
    }

    [Fact]
    public async Task TraverseAsync_AllowsManualOverride_OnNonWindowsHostsWithoutThrowing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var adapter = new ReflectionTiaProjectOpennessAdapter(
            new StubDiscoveryService(Array.Empty<DiscoveredTiaPortalInstallation>()),
            Array.Empty<ITiaDomainExtractor>(),
            NullLogger<ReflectionTiaProjectOpennessAdapter>.Instance);

        var result = await adapter.TraverseAsync(
            "/tmp/sample.ap20",
            @"C:\Program Files\Siemens\Automation\Portal V20",
            CancellationToken.None);

        Assert.NotEmpty(result.Objects);
        Assert.Contains(result.Issues, issue => issue.Scope == "OpennessRuntime");
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
