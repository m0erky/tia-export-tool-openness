namespace TiaProjectExporter.Core.Models;

/// <summary>
/// User-selected options that control export output.
/// </summary>
public sealed record ExportOptions(
    string OutputDirectory,
    IReadOnlyCollection<ExportFormat> Formats,
    bool EnableCompression,
    bool SkipDiagnostics,
    bool GenerateMarkdownSummaries)
{
    /// <summary>
    /// Creates default options for an export session.
    /// </summary>
    public static ExportOptions CreateDefault(string outputDirectory) =>
        new(
            outputDirectory,
            new[] { ExportFormat.Json, ExportFormat.Xml, ExportFormat.Markdown },
            EnableCompression: false,
            SkipDiagnostics: false,
            GenerateMarkdownSummaries: true);
}

