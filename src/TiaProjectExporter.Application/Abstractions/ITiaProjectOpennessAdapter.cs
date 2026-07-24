using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Application.Abstractions;

/// <summary>
/// Defines a low-level adapter for Siemens TIA Openness project traversal.
/// </summary>
public interface ITiaProjectOpennessAdapter
{
    /// <summary>
    /// Traverses a TIA project and returns discovered objects plus adapter-level issues.
    /// </summary>
    Task<TiaProjectTraversalResult> TraverseAsync(string projectPath, CancellationToken cancellationToken);
}
