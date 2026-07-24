namespace TiaProjectExporter.Application.Abstractions;

/// <summary>
/// Creates compressed archive packages for completed exports.
/// </summary>
public interface IExportArchiveService
{
    /// <summary>
    /// Creates an archive for a source directory and returns the generated archive path.
    /// </summary>
    Task<string> CreateArchiveAsync(
        string outputRoot,
        string sourceDirectoryName,
        string archiveFileName,
        CancellationToken cancellationToken);
}

