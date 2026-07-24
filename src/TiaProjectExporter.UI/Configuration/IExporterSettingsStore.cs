namespace TiaProjectExporter.UI.Configuration;

/// <summary>
/// Provides persistence for user-specific exporter settings.
/// </summary>
public interface IExporterSettingsStore
{
    /// <summary>
    /// Loads persisted settings, or defaults when no file exists.
    /// </summary>
    PersistedExporterSettings Load();

    /// <summary>
    /// Saves persisted settings.
    /// </summary>
    Task SaveAsync(PersistedExporterSettings settings, CancellationToken cancellationToken);
}
