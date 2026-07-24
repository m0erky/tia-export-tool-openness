namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Represents a single file to be written into the export repository.
/// </summary>
public sealed record ExportArtifact(string RelativePath, ExportFormat Format, string Content);

