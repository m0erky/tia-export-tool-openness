namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Represents a recoverable issue encountered during export.
/// </summary>
public sealed record ExportIssue(string Scope, string Message, string? Details = null);

