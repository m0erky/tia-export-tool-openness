using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Application.Abstractions;

/// <summary>
/// Writes artifacts and directories into the export repository.
/// </summary>
public interface IExportArtifactWriter
{
    /// <summary>
    /// Ensures a directory exists relative to the export root.
    /// </summary>
    Task EnsureDirectoryAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a single artifact relative to the export root.
    /// </summary>
    Task WriteArtifactAsync(ExportArtifact artifact, CancellationToken cancellationToken);
}

