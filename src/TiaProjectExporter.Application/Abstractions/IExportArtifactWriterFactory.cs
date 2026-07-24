namespace TiaProjectExporter.Application.Abstractions;

/// <summary>
/// Creates artifact writers for a specific export root.
/// </summary>
public interface IExportArtifactWriterFactory
{
    /// <summary>
    /// Creates a writer for the provided output root.
    /// </summary>
    IExportArtifactWriter Create(string outputRoot);
}

