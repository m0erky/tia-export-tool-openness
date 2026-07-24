using System.IO.Compression;
using TiaProjectExporter.Application.Abstractions;

namespace TiaProjectExporter.Infrastructure.Writers;

/// <summary>
/// Creates ZIP archives for completed export directories.
/// </summary>
public sealed class ZipExportArchiveService : IExportArchiveService
{
    /// <inheritdoc />
    public Task<string> CreateArchiveAsync(
        string outputRoot,
        string sourceDirectoryName,
        string archiveFileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourceDirectory = Path.Combine(outputRoot, sourceDirectoryName);
        var archivePath = Path.Combine(outputRoot, archiveFileName);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source export directory does not exist: {sourceDirectory}");
        }

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return Task.FromResult(archivePath);
    }
}
