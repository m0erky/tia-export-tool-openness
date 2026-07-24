namespace TiaProjectExporter.Export.Repository;

/// <summary>
/// Defines the standard repository layout created by the exporter.
/// </summary>
public static class ExportRepositoryLayout
{
    /// <summary>
    /// Required top-level directories for a generated export repository.
    /// </summary>
    public static IReadOnlyList<string> Directories { get; } =
    [
        "Export",
        "Export/Project",
        "Export/Hardware",
        "Export/Network",
        "Export/PLC",
        "Export/Blocks",
        "Export/Tags",
        "Export/UDTs",
        "Export/Technology",
        "Export/Libraries",
        "Export/HMI",
        "Export/Diagnostics",
        "Export/Metadata",
        "Export/Reports"
    ];
}

