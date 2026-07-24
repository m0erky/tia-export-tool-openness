namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Represents an installed TIA Portal version discovered on the current machine.
/// </summary>
/// <param name="Version">Installed major version.</param>
/// <param name="DisplayName">Display label for the UI and reports.</param>
/// <param name="InstallPath">Installation path when known.</param>
/// <param name="OpennessAvailable">Whether Openness integration appears available.</param>
public sealed record DiscoveredTiaPortalInstallation(
    TiaPortalVersion Version,
    string DisplayName,
    string? InstallPath,
    bool OpennessAvailable);

