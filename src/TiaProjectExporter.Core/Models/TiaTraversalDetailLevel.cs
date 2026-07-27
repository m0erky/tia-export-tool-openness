namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Controls traversal depth for Openness inventory operations.
/// </summary>
public enum TiaTraversalDetailLevel
{
    /// <summary>
    /// Lightweight preview scan for quick domain discovery.
    /// </summary>
    Preview,

    /// <summary>
    /// Full traversal for actual export.
    /// </summary>
    Full
}

