namespace TiaProjectExporter.UI.Configuration;

/// <summary>
/// UI-facing settings loaded from appsettings.json.
/// </summary>
public sealed class ExporterSettings
{
    /// <summary>
    /// Gets or sets the default output directory.
    /// </summary>
    public string DefaultOutputDirectory { get; set; } = "GeneratedExport";

    /// <summary>
    /// Gets or sets a value indicating whether markdown summaries are enabled by default.
    /// </summary>
    public bool GenerateMarkdownSummaries { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether compression is enabled by default.
    /// </summary>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether diagnostics should be skipped by default.
    /// </summary>
    public bool SkipDiagnostics { get; set; }
}

