using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaProjectExporter.Infrastructure.Serialization;

/// <summary>
/// Produces shared JSON serializer settings for exported files.
/// </summary>
public static class JsonOptionsFactory
{
    /// <summary>
    /// Creates the default options used across exporter outputs.
    /// </summary>
    public static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

