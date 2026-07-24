using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TiaProjectExporter.UI.Configuration;

/// <summary>
/// JSON file implementation of <see cref="IExporterSettingsStore"/>.
/// </summary>
public sealed class JsonExporterSettingsStore : IExporterSettingsStore
{
    private const int MaxRecentDirectories = 10;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<JsonExporterSettingsStore> _logger;
    private readonly string _settingsFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonExporterSettingsStore"/> class.
    /// </summary>
    public JsonExporterSettingsStore(ILogger<JsonExporterSettingsStore> logger)
    {
        _logger = logger;

        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TiaProjectExporter");

        _settingsFilePath = Path.Combine(settingsDirectory, "user-settings.json");
    }

    /// <inheritdoc />
    public PersistedExporterSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new PersistedExporterSettings();
            }

            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<PersistedExporterSettings>(json, SerializerOptions) ?? new PersistedExporterSettings();
            settings.RecentOutputDirectories = NormalizeRecentDirectories(settings.RecentOutputDirectories);
            return settings;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load persisted user settings from {SettingsPath}", _settingsFilePath);
            return new PersistedExporterSettings();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(PersistedExporterSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            settings.RecentOutputDirectories = NormalizeRecentDirectories(settings.RecentOutputDirectories);
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to persist user settings to {SettingsPath}", _settingsFilePath);
        }
    }

    private static List<string> NormalizeRecentDirectories(IEnumerable<string>? recentDirectories) =>
        (recentDirectories ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentDirectories)
            .ToList();
}
