namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Structured result for Openness host/runtime health validation.
/// </summary>
public sealed record OpennessHealthCheckResult(
    OpennessHealthCheckState State,
    string Summary,
    IReadOnlyList<string> Details);

