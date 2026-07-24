using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Application.Abstractions;

/// <summary>
/// Discovers locally installed TIA Portal versions supported by the exporter.
/// </summary>
public interface ITiaInstallationDiscoveryService
{
    /// <summary>
    /// Discovers supported TIA Portal installations.
    /// </summary>
    Task<IReadOnlyList<DiscoveredTiaPortalInstallation>> DiscoverAsync(CancellationToken cancellationToken);
}

