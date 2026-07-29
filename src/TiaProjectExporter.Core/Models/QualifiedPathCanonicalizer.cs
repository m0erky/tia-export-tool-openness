namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Canonicalizes discovered runtime paths so semantically identical nodes share one stable path.
/// </summary>
public static class QualifiedPathCanonicalizer
{
    /// <summary>
    /// Canonicalizes a runtime qualified path.
    /// </summary>
    public static string Canonicalize(string? qualifiedPath)
    {
        if (string.IsNullOrWhiteSpace(qualifiedPath))
        {
            return string.Empty;
        }

        var segments = qualifiedPath
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (segments.Count == 0)
        {
            return string.Empty;
        }

        CollapseDuplicateDeviceItemImplSegments(segments);
        RemoveBlockGroupBlocksSegments(segments);

        return string.Join('/', segments);
    }

    private static void CollapseDuplicateDeviceItemImplSegments(List<string> segments)
    {
        for (var index = 1; index < segments.Count; index++)
        {
            if (!segments[index - 1].Equals("DeviceItemImpl", StringComparison.OrdinalIgnoreCase)
                || !segments[index].Equals("DeviceItemImpl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            segments.RemoveAt(index);
            index--;
        }
    }

    private static void RemoveBlockGroupBlocksSegments(List<string> segments)
    {
        for (var index = 1; index < segments.Count; index++)
        {
            if (!segments[index - 1].Equals("BlockGroup", StringComparison.OrdinalIgnoreCase)
                || !segments[index].Equals("Blocks", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            segments.RemoveAt(index);
            index--;
        }
    }
}

