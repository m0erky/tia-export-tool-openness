using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory;

/// <summary>
/// Validates out-of-process Openness host availability and runtime compatibility.
/// </summary>
public sealed class OutOfProcessOpennessHealthCheckService : IOpennessHealthCheckService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITiaInstallationDiscoveryService _installationDiscoveryService;
    private readonly ILogger<OutOfProcessOpennessHealthCheckService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutOfProcessOpennessHealthCheckService"/> class.
    /// </summary>
    public OutOfProcessOpennessHealthCheckService(
        ITiaInstallationDiscoveryService installationDiscoveryService,
        ILogger<OutOfProcessOpennessHealthCheckService> logger)
    {
        _installationDiscoveryService = installationDiscoveryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OpennessHealthCheckResult> CheckAsync(string? tiaInstallationPathOverride, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var details = new List<string>();

        if (!OperatingSystem.IsWindows())
        {
            details.Add("Health check is only supported on Windows hosts.");
            return new OpennessHealthCheckResult(OpennessHealthCheckState.Unhealthy, "Platform not supported", details);
        }

        var hostPath = OutOfProcessHostLocator.ResolveHostExecutablePath();

        if (hostPath is null)
        {
            details.Add("Openness host executable not found.");
            details.Add($"Set {OutOfProcessHostLocator.HostPathEnvironmentVariable} or deploy TiaProjectExporter.OpennessHost.exe next to UI executable.");
            return new OpennessHealthCheckResult(OpennessHealthCheckState.Unhealthy, "Openness host missing", details);
        }

        details.Add($"Host executable: {hostPath}");

        var installations = await _installationDiscoveryService.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var preferredInstallation = ResolvePreferredInstallation(installations, tiaInstallationPathOverride);

        if (preferredInstallation is null || string.IsNullOrWhiteSpace(preferredInstallation.InstallPath))
        {
            details.Add("No valid TIA installation path available for host health check.");
            return new OpennessHealthCheckResult(OpennessHealthCheckState.Unhealthy, "TIA installation not detected", details);
        }

        details.Add($"Selected installation: {preferredInstallation.DisplayName}");
        details.Add($"Installation path: {preferredInstallation.InstallPath}");

        var healthResponse = await ExecuteHostHealthAsync(hostPath, preferredInstallation.InstallPath, cancellationToken).ConfigureAwait(false);

        if (healthResponse is null)
        {
            details.Add("Host returned no parsable health response.");
            return new OpennessHealthCheckResult(OpennessHealthCheckState.Unhealthy, "Host response invalid", details);
        }

        details.AddRange(healthResponse.Details ?? []);

        if (healthResponse.Healthy)
        {
            var hasContractWarning = details.Any(detail =>
                detail.Contains("Contract.dll not found", StringComparison.OrdinalIgnoreCase));

            if (hasContractWarning)
            {
                return new OpennessHealthCheckResult(OpennessHealthCheckState.Warning, "Openness host is reachable, but contract assembly warning detected", details);
            }

            return new OpennessHealthCheckResult(OpennessHealthCheckState.Healthy, "Openness host is healthy", details);
        }

        return new OpennessHealthCheckResult(OpennessHealthCheckState.Unhealthy, "Openness host check failed", details);
    }

    private static DiscoveredTiaPortalInstallation? ResolvePreferredInstallation(
        IReadOnlyList<DiscoveredTiaPortalInstallation> discoveredInstallations,
        string? tiaInstallationPathOverride)
    {
        if (!string.IsNullOrWhiteSpace(tiaInstallationPathOverride))
        {
            var normalizedPath = tiaInstallationPathOverride.Trim().Trim('"');
            var version = InferVersionFromPath(normalizedPath);

            return new DiscoveredTiaPortalInstallation(
                version,
                $"Manual TIA Override ({version})",
                normalizedPath,
                OpennessAvailable: true);
        }

        return discoveredInstallations
            .Where(installation => installation.OpennessAvailable && !string.IsNullOrWhiteSpace(installation.InstallPath))
            .OrderByDescending(installation => installation.Version)
            .FirstOrDefault();
    }

    private static TiaPortalVersion InferVersionFromPath(string installPath)
    {
        if (installPath.Contains("V18", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains("Portal V18", StringComparison.OrdinalIgnoreCase))
        {
            return TiaPortalVersion.V18;
        }

        if (installPath.Contains("V19", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains("Portal V19", StringComparison.OrdinalIgnoreCase))
        {
            return TiaPortalVersion.V19;
        }

        return TiaPortalVersion.V20;
    }

    private async Task<HostHealthResponse?> ExecuteHostHealthAsync(
        string hostPath,
        string installPath,
        CancellationToken cancellationToken)
    {
        var arguments = BuildHealthArguments(installPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory
        };

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start Openness host health process.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("Openness health host exited with code {ExitCode}. STDERR: {Stderr}", process.ExitCode, standardError);
            throw new InvalidOperationException($"Openness host health exited with code {process.ExitCode}. STDERR: {standardError}");
        }

        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            throw new InvalidOperationException("Openness host health returned empty output.");
        }

        return JsonSerializer.Deserialize<HostHealthResponse>(standardOutput, SerializerOptions);
    }

    private static string BuildHealthArguments(string installPath)
    {
        var builder = new StringBuilder();
        builder.Append("--health ");
        builder.Append("--install ").Append(Quote(installPath));
        return builder.ToString();
    }

    private static string Quote(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "\"\""
            : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private sealed class HostHealthResponse
    {
        public bool Healthy { get; set; }

        public List<string>? Details { get; set; }
    }
}
