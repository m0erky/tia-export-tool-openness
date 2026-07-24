using TiaProjectExporter.Application.Abstractions;

namespace TiaProjectExporter.Infrastructure.Writers;

/// <summary>
/// Creates file-system-backed artifact writers for export runs.
/// </summary>
public sealed class FileSystemExportArtifactWriterFactory : IExportArtifactWriterFactory
{
    /// <inheritdoc />
    public IExportArtifactWriter Create(string outputRoot) => new FileSystemExportArtifactWriter(outputRoot);
}

