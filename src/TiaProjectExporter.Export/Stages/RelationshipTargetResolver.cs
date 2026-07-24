namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Shared resolver for dependency/call relationship targets.
/// </summary>
internal static class RelationshipTargetResolver
{
    public static bool IsResolvedTarget(string target, IReadOnlySet<string> nodeIds, IReadOnlySet<string> nodeNames)
    {
        var normalized = NormalizeTarget(target);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (nodeIds.Contains(normalized) || nodeNames.Contains(normalized))
        {
            return true;
        }

        if (nodeIds.Any(nodeId => nodeId.EndsWith($"/{normalized}", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (normalized.Contains('/', StringComparison.Ordinal)
            && nodeIds.Any(nodeId => nodeId.Equals(normalized.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var shortName = ExtractShortName(normalized);
        return !string.IsNullOrWhiteSpace(shortName)
            && (nodeNames.Contains(shortName)
                || nodeIds.Any(nodeId => nodeId.EndsWith($"/{shortName}", StringComparison.OrdinalIgnoreCase)));
    }

    public static string NormalizeTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        var normalized = target.Trim();

        var arrowIndex = normalized.IndexOf("->", StringComparison.Ordinal);
        if (arrowIndex >= 0 && arrowIndex + 2 < normalized.Length)
        {
            normalized = normalized[(arrowIndex + 2)..].Trim();
        }

        if (normalized.StartsWith("\"", StringComparison.Ordinal) && normalized.EndsWith("\"", StringComparison.Ordinal) && normalized.Length > 1)
        {
            normalized = normalized[1..^1];
        }

        return normalized.Replace('\\', '/');
    }

    private static string ExtractShortName(string normalized)
    {
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash + 1 < normalized.Length)
        {
            return normalized[(lastSlash + 1)..];
        }

        var lastDot = normalized.LastIndexOf('.');
        if (lastDot >= 0 && lastDot + 1 < normalized.Length)
        {
            return normalized[(lastDot + 1)..];
        }

        return normalized;
    }
}
