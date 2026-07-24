using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Infrastructure.Writers;

/// <summary>
/// Persists export artifacts onto the local file system.
/// </summary>
public sealed class FileSystemExportArtifactWriter : IExportArtifactWriter
{
    private readonly string _outputRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemExportArtifactWriter"/> class.
    /// </summary>
    public FileSystemExportArtifactWriter(string outputRoot)
    {
        _outputRoot = outputRoot;
    }

    /// <inheritdoc />
    public Task EnsureDirectoryAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.Combine(_outputRoot, relativePath));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task WriteArtifactAsync(ExportArtifact artifact, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(_outputRoot, artifact.RelativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, artifact.Content, cancellationToken).ConfigureAwait(false);
    }
}

