using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Application.Abstractions;

/// <summary>
/// Validates whether out-of-process Openness execution is available and healthy.
/// </summary>
public interface IOpennessHealthCheckService
{
    /// <summary>
    /// Executes the runtime health check.
    /// </summary>
    Task<OpennessHealthCheckResult> CheckAsync(string? tiaInstallationPathOverride, CancellationToken cancellationToken);
}

