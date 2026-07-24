using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.Versioning;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Discovery;

/// <summary>
/// Discovers supported TIA Portal installations via the Windows registry.
/// </summary>
public sealed class RegistryTiaInstallationDiscoveryService : ITiaInstallationDiscoveryService
{
    private static readonly IReadOnlyDictionary<TiaPortalVersion, string[]> VersionKeyCandidates = new Dictionary<TiaPortalVersion, string[]>
    {
        [TiaPortalVersion.V18] =
        [
            @"SOFTWARE\Siemens\Automation\Portal V18",
            @"SOFTWARE\Siemens\Automation\Totally Integrated Automation Portal\V18"
        ],
        [TiaPortalVersion.V19] =
        [
            @"SOFTWARE\Siemens\Automation\Portal V19",
            @"SOFTWARE\Siemens\Automation\Totally Integrated Automation Portal\V19"
        ],
        [TiaPortalVersion.V20] =
        [
            @"SOFTWARE\Siemens\Automation\Portal V20",
            @"SOFTWARE\Siemens\Automation\Totally Integrated Automation Portal\V20"
        ]
    };

    private static readonly string[] InstallationPathValueCandidates =
    [
        "InstallPath",
        "InstallDir",
        "Path",
        "InstallationPath",
        "InstallationDirectory",
        "BinPath",
        "InstallLocation"
    ];

    private static readonly string[] UninstallKeyCandidates =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

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

        foreach (var (version, keyPaths) in VersionKeyCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var installation = TryReadInstallationFromKnownKeys(version, keyPaths, RegistryHive.LocalMachine, RegistryView.Registry64)
                ?? TryReadInstallationFromKnownKeys(version, keyPaths, RegistryHive.LocalMachine, RegistryView.Registry32)
                ?? TryReadInstallationFromKnownKeys(version, keyPaths, RegistryHive.CurrentUser, RegistryView.Registry64)
                ?? TryReadInstallationFromKnownKeys(version, keyPaths, RegistryHive.CurrentUser, RegistryView.Registry32)
                ?? TryReadInstallationFromSiemensTree(version, RegistryHive.LocalMachine, RegistryView.Registry64)
                ?? TryReadInstallationFromSiemensTree(version, RegistryHive.LocalMachine, RegistryView.Registry32)
                ?? TryReadInstallationFromSiemensTree(version, RegistryHive.CurrentUser, RegistryView.Registry64)
                ?? TryReadInstallationFromSiemensTree(version, RegistryHive.CurrentUser, RegistryView.Registry32)
                ?? TryReadInstallationFromUninstall(version, RegistryHive.LocalMachine, RegistryView.Registry64)
                ?? TryReadInstallationFromUninstall(version, RegistryHive.LocalMachine, RegistryView.Registry32)
                ?? TryReadInstallationFromUninstall(version, RegistryHive.CurrentUser, RegistryView.Registry64)
                ?? TryReadInstallationFromUninstall(version, RegistryHive.CurrentUser, RegistryView.Registry32);

            if (installation is not null)
            {
                results.Add(installation);
            }
        }

        return Task.FromResult<IReadOnlyList<DiscoveredTiaPortalInstallation>>(results);
    }

    [SupportedOSPlatform("windows")]
    private DiscoveredTiaPortalInstallation? TryReadInstallationFromKnownKeys(
        TiaPortalVersion version,
        IEnumerable<string> subKeyPaths,
        RegistryHive hive,
        RegistryView view)
    {
        foreach (var subKeyPath in subKeyPaths)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var subKey = baseKey.OpenSubKey(subKeyPath);

                if (subKey is null)
                {
                    continue;
                }

                var installPath = ReadFirstString(subKey, InstallationPathValueCandidates);
                if (string.IsNullOrWhiteSpace(installPath))
                {
                    continue;
                }

                var normalizedInstallPath = NormalizeInstallPath(installPath);
                var displayName = $"TIA Portal {version}";
                var opennessAvailable = LooksLikeOpennessInstall(normalizedInstallPath);

                if (!IsLikelyPortalInstallation(displayName, VersionToToken(version), normalizedInstallPath, opennessAvailable))
                {
                    continue;
                }

                return new DiscoveredTiaPortalInstallation(version, displayName, normalizedInstallPath, opennessAvailable);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Failed to inspect {Hive} registry view {RegistryView} for {Version} using key {SubKey}", hive, view, version, subKeyPath);
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private DiscoveredTiaPortalInstallation? TryReadInstallationFromUninstall(
        TiaPortalVersion version,
        RegistryHive hive,
        RegistryView view)
    {
        var versionToken = VersionToToken(version);

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);

            foreach (var uninstallRoot in UninstallKeyCandidates)
            {
                using var uninstallKey = baseKey.OpenSubKey(uninstallRoot);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var productSubKeyName in uninstallKey.GetSubKeyNames())
                {
                    using var productSubKey = uninstallKey.OpenSubKey(productSubKeyName);
                    if (productSubKey is null)
                    {
                        continue;
                    }

                    var displayName = ReadFirstString(productSubKey, "DisplayName") ?? string.Empty;
                    if (!displayName.Contains("TIA Portal", StringComparison.OrdinalIgnoreCase)
                        || !displayName.Contains(versionToken, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var installPath = ReadFirstString(productSubKey, InstallationPathValueCandidates);
                    if (string.IsNullOrWhiteSpace(installPath))
                    {
                        continue;
                    }

                    var normalizedInstallPath = NormalizeInstallPath(installPath);
                    var opennessAvailable = LooksLikeOpennessInstall(normalizedInstallPath);

                    if (!IsLikelyPortalInstallation(displayName, versionToken, normalizedInstallPath, opennessAvailable))
                    {
                        continue;
                    }

                    return new DiscoveredTiaPortalInstallation(version, displayName, normalizedInstallPath, opennessAvailable);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed uninstall-based registry probing for {Version} on {Hive}/{RegistryView}", version, hive, view);
        }

        return null;
    }

    private static string VersionToToken(TiaPortalVersion version) =>
        version switch
        {
            TiaPortalVersion.V18 => "V18",
            TiaPortalVersion.V19 => "V19",
            TiaPortalVersion.V20 => "V20",
            _ => version.ToString()
        };

    private static string NormalizeInstallPath(string installPath)
    {
        var normalized = installPath.Trim().Trim('"');

        if (normalized.EndsWith("Siemens.Engineering.dll", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(normalized);
            return string.IsNullOrWhiteSpace(directory) ? normalized : directory;
        }

        return normalized;
    }

    private static bool LooksLikeOpennessInstall(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return false;
        }

        try
        {
            var normalized = installPath.Trim();

            if (File.Exists(Path.Combine(normalized, "Siemens.Engineering.dll")))
            {
                return true;
            }

            return File.Exists(Path.Combine(normalized, "PublicAPI", "Siemens.Engineering.dll"))
                || File.Exists(Path.Combine(normalized, "Bin", "Siemens.Engineering.dll"))
                || File.Exists(Path.Combine(normalized, "PublicAPI", "V18", "Siemens.Engineering.dll"))
                || File.Exists(Path.Combine(normalized, "PublicAPI", "V19", "Siemens.Engineering.dll"))
                || File.Exists(Path.Combine(normalized, "PublicAPI", "V20", "Siemens.Engineering.dll"));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLikelyPortalInstallation(string displayName, string versionToken, string installPath, bool opennessAvailable)
    {
        if (string.IsNullOrWhiteSpace(displayName)
            || !displayName.Contains("TIA Portal", StringComparison.OrdinalIgnoreCase)
            || !displayName.Contains(versionToken, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (displayName.Contains("Help Viewer", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Viewer", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Documentation", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Readme", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Updater", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Update Service", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (opennessAvailable)
        {
            return true;
        }

        return installPath.Contains("Portal V", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains("Totally Integrated Automation Portal", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains("TIA Portal", StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    private DiscoveredTiaPortalInstallation? TryReadInstallationFromSiemensTree(
        TiaPortalVersion version,
        RegistryHive hive,
        RegistryView view)
    {
        var versionToken = VersionToToken(version);

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(@"SOFTWARE\Siemens\Automation");

            if (root is null)
            {
                return null;
            }

            foreach (var relativePath in EnumerateSubKeyPaths(root, parentPath: string.Empty, maxDepth: 4))
            {
                using var subKey = root.OpenSubKey(relativePath);
                if (subKey is null)
                {
                    continue;
                }

                var keyPath = subKey.Name ?? string.Empty;

                if (!keyPath.Contains("Portal", StringComparison.OrdinalIgnoreCase)
                    && !keyPath.Contains("Totally Integrated Automation", StringComparison.OrdinalIgnoreCase)
                    && !keyPath.Contains(versionToken, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var installPath = ReadFirstString(subKey, InstallationPathValueCandidates);
                if (string.IsNullOrWhiteSpace(installPath))
                {
                    continue;
                }

                var normalizedInstallPath = NormalizeInstallPath(installPath);
                var opennessAvailable = LooksLikeOpennessInstall(normalizedInstallPath);
                var displayName = ReadFirstString(subKey, "DisplayName", "ProductName") ?? $"TIA Portal {version}";

                if (!IsLikelyPortalInstallation(displayName, versionToken, normalizedInstallPath, opennessAvailable))
                {
                    continue;
                }

                return new DiscoveredTiaPortalInstallation(version, displayName, normalizedInstallPath, opennessAvailable);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed Siemens tree probing for {Version} on {Hive}/{RegistryView}", version, hive, view);
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> EnumerateSubKeyPaths(RegistryKey root, string parentPath, int maxDepth)
    {
        if (maxDepth < 0)
        {
            yield break;
        }

        foreach (var name in root.GetSubKeyNames())
        {
            var currentPath = string.IsNullOrWhiteSpace(parentPath)
                ? name
                : $"{parentPath}\\{name}";

            yield return currentPath;

            if (maxDepth <= 0)
            {
                continue;
            }

            RegistryKey? subKey;

            try
            {
                subKey = root.OpenSubKey(name);
            }
            catch
            {
                subKey = null;
            }

            if (subKey is null)
            {
                continue;
            }

            using (subKey)
            {
                foreach (var nested in EnumerateSubKeyPaths(subKey, currentPath, maxDepth - 1))
                {
                    yield return nested;
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
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
