namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Metadata for a generated compressed export archive.
/// </summary>
public sealed record ExportArchiveInfo(
    string ArchivePath,
    long? SizeBytes,
    string? Sha256,
    DateTimeOffset GeneratedAt);

