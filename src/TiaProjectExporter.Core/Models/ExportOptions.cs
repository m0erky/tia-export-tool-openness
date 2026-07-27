namespace TiaProjectExporter.Core.Models;

/// <summary>
/// User-selected options that control export output.
/// </summary>
public sealed record ExportOptions(
    string? ProjectPath,
    string OutputDirectory,
    IReadOnlyCollection<ExportFormat> Formats,
    bool EnableCompression,
    bool SkipDiagnostics,
    bool GenerateMarkdownSummaries,
    string? TiaInstallationPathOverride = null,
    IReadOnlyCollection<ExportDomain>? IncludedDomains = null)
{
    /// <summary>
    /// Creates default options for an export session.
    /// </summary>
    public static ExportOptions CreateDefault(string outputDirectory) =>
        new(
            ProjectPath: null,
            OutputDirectory: outputDirectory,
            Formats: new[] { ExportFormat.Json, ExportFormat.Xml, ExportFormat.Markdown },
            EnableCompression: false,
            SkipDiagnostics: false,
            GenerateMarkdownSummaries: true,
            TiaInstallationPathOverride: null,
            IncludedDomains: null);
}
