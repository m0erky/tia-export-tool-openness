namespace TiaProjectExporter.UI.Configuration;

/// <summary>
/// Persisted user-specific exporter preferences.
/// </summary>
public sealed class PersistedExporterSettings
{
    /// <summary>
    /// Gets or sets the last selected source project path.
    /// </summary>
    public string? LastProjectPath { get; set; }

    /// <summary>
    /// Gets or sets the last selected output directory.
    /// </summary>
    public string? LastOutputDirectory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether JSON export is selected.
    /// </summary>
    public bool ExportJson { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether XML export is selected.
    /// </summary>
    public bool ExportXml { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Markdown export is selected.
    /// </summary>
    public bool ExportMarkdown { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether compression is selected.
    /// </summary>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether diagnostics should be skipped.
    /// </summary>
    public bool SkipDiagnostics { get; set; }

    /// <summary>
    /// Gets or sets the most recent output directories.
    /// </summary>
    public List<string> RecentOutputDirectories { get; set; } = [];
}
