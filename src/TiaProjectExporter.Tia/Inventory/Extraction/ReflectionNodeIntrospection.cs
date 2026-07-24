using System.Collections;
using System.Reflection;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Helper methods for reflection-based runtime node introspection.
/// </summary>
internal static class ReflectionNodeIntrospection
{
    public static string? TryReadString(object node, string propertyName)
    {
        try
        {
            var property = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            var value = property?.GetValue(node);
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public static string[] ExtractNamedReferences(object node, params string[] propertyNames)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var propertyName in propertyNames)
        {
            object? value;

            try
            {
                var property = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                value = property?.GetValue(node);
            }
            catch
            {
                continue;
            }

            if (value is null)
            {
                continue;
            }

            if (value is string stringValue)
            {
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    names.Add(stringValue.Trim());
                }

                continue;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var name = item is null ? null : TryReadString(item, "Name") ?? item.ToString();

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name.Trim());
                    }
                }

                continue;
            }

            var singleName = TryReadString(value, "Name") ?? value.ToString();
            if (!string.IsNullOrWhiteSpace(singleName))
            {
                names.Add(singleName.Trim());
            }
        }

        return names.ToArray();
    }

    public static double CalculateConfidence(object node, string objectType)
    {
        var score = 0.35;

        var runtimeTypeName = node.GetType().Name;
        if (runtimeTypeName.Contains(objectType, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.30;
        }

        var name = TryReadString(node, "Name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            score += 0.15;
        }

        if (!string.IsNullOrWhiteSpace(TryReadString(node, "DisplayName")))
        {
            score += 0.05;
        }

        var metadataHints = 0;

        if (!string.IsNullOrWhiteSpace(TryReadString(node, "Comment")))
        {
            metadataHints++;
        }

        if (!string.IsNullOrWhiteSpace(TryReadString(node, "Description")))
        {
            metadataHints++;
        }

        if (!string.IsNullOrWhiteSpace(TryReadString(node, "Title")))
        {
            metadataHints++;
        }

        if (metadataHints > 0)
        {
            score += Math.Min(0.15, metadataHints * 0.05);
        }

        return Math.Clamp(score, 0.0, 0.99);
    }

    public static IReadOnlyDictionary<string, string> BuildCommonMetadata(object node, string objectType)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RuntimeType"] = node.GetType().FullName ?? node.GetType().Name,
            ["ExtractionConfidence"] = CalculateConfidence(node, objectType).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            ["ExtractionStrategy"] = "ReflectionHeuristic"
        };

        AddMetadataIfPresent(node, metadata, "Comment", "Comment");
        AddMetadataIfPresent(node, metadata, "Title", "Title");
        AddMetadataIfPresent(node, metadata, "Description", "Description");
        AddMetadataIfPresent(node, metadata, "Text", "Text");
        AddMetadataIfPresent(node, metadata, "TextDe", "Text_de-DE");
        AddMetadataIfPresent(node, metadata, "TextEn", "Text_en-US");
        AddMetadataIfPresent(node, metadata, "CommentDe", "Comment_de-DE");
        AddMetadataIfPresent(node, metadata, "CommentEn", "Comment_en-US");

        return metadata;
    }

    private static void AddMetadataIfPresent(object node, IDictionary<string, string> metadata, string propertyName, string metadataKey)
    {
        var value = TryReadString(node, propertyName);

        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[metadataKey] = value;
        }
    }
}
