using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Application.Abstractions;

/// <summary>
/// Produces a traversal-friendly inventory of a TIA project.
/// </summary>
public interface ITiaProjectInventoryProvider
{
    /// <summary>
    /// Builds a project inventory for the supplied project path.
    /// </summary>
    Task<TiaProjectInventory> BuildInventoryAsync(string? projectPath, string? tiaInstallationPathOverride, CancellationToken cancellationToken);
}
