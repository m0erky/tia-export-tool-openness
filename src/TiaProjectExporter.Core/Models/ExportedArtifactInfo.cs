namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Metadata describing an artifact written during an export run.
/// </summary>
public sealed record ExportedArtifactInfo(
    string RelativePath,
    ExportFormat Format,
    int ContentLength,
    DateTimeOffset WrittenAt);

