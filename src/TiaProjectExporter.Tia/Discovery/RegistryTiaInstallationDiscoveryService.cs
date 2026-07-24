using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Discovery;

/// <summary>
/// Discovers supported TIA Portal installations via the Windows registry.
/// </summary>
public sealed class RegistryTiaInstallationDiscoveryService : ITiaInstallationDiscoveryService
{
    private static readonly IReadOnlyDictionary<TiaPortalVersion, string> VersionKeys = new Dictionary<TiaPortalVersion, string>
    {
        [TiaPortalVersion.V18] = @"SOFTWARE\Siemens\Automation\Portal V18",
        [TiaPortalVersion.V19] = @"SOFTWARE\Siemens\Automation\Portal V19",
        [TiaPortalVersion.V20] = @"SOFTWARE\Siemens\Automation\Portal V20"
    };

    private readonly ILogger<RegistryTiaInstallationDiscoveryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryTiaInstallationDiscoveryService"/> class.
    /// </summary>
    public RegistryTiaInstallationDiscoveryService(ILogger<RegistryTiaInstallationDiscoveryService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredTiaPortalInstallation>> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("TIA installation discovery is only available on Windows hosts");
            return Task.FromResult<IReadOnlyList<DiscoveredTiaPortalInstallation>>([]);
        }

        var results = new List<DiscoveredTiaPortalInstallation>();

        foreach (var (version, keyPath) in VersionKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var installation = TryReadInstallation(version, keyPath, RegistryView.Registry64)
                ?? TryReadInstallation(version, keyPath, RegistryView.Registry32);

            if (installation is not null)
            {
                results.Add(installation);
            }
        }

        return Task.FromResult<IReadOnlyList<DiscoveredTiaPortalInstallation>>(results);
    }

    private DiscoveredTiaPortalInstallation? TryReadInstallation(
        TiaPortalVersion version,
        string subKeyPath,
        RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var subKey = baseKey.OpenSubKey(subKeyPath);

            if (subKey is null)
            {
                return null;
            }

            var installPath = ReadFirstString(subKey, "InstallPath", "Path", "InstallationPath", "BinPath");
            var displayName = $"TIA Portal {version}";
            var opennessAvailable = installPath is not null;

            return new DiscoveredTiaPortalInstallation(version, displayName, installPath, opennessAvailable);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to inspect registry view {RegistryView} for {Version}", view, version);
            return null;
        }
    }

    private static string? ReadFirstString(RegistryKey registryKey, params string[] candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            if (registryKey.GetValue(candidateName) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

