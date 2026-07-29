using System.Xml.Linq;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Extracts block-call relationships from metadata and export XML payloads.
/// </summary>
internal static class CallRelationshipExtractor
{
    private static readonly char[] Separators = [',', ';', '|'];
    private static readonly string[] CallMetadataKeys = ["Calls", "BlockCalls", "InvokedBlocks", "CalledBlocks"];

    public static IReadOnlyDictionary<string, string> BuildInstanceTargetMap(TiaProjectInventory inventory)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in inventory.Objects)
        {
            var metadata = node.Metadata;
            if (metadata is null)
            {
                continue;
            }

            var instanceOf = ReadFirstNonEmpty(metadata, "InstanceOfName", "InstanceOf", "DataType");
            if (string.IsNullOrWhiteSpace(instanceOf) && !node.ObjectType.Contains("InstanceDB", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var instanceName = RelationshipTargetResolver.NormalizeTarget(node.Name);
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                continue;
            }

            var target = RelationshipTargetResolver.NormalizeTarget(instanceOf);
            if (string.IsNullOrWhiteSpace(target) && instanceName.EndsWith("_DB", StringComparison.OrdinalIgnoreCase))
            {
                target = instanceName[..^3];
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            map[instanceName] = target;
        }

        return map;
    }

    public static IReadOnlyList<ExtractedCallRelation> ExtractCallRelations(
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyDictionary<string, string> instanceTargetMap)
    {
        var relations = new List<ExtractedCallRelation>();

        foreach (var key in CallMetadataKeys)
        {
            if (metadata is null || !metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var token in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var resolved = ResolveTarget(token, instanceTargetMap);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    continue;
                }

                relations.Add(new ExtractedCallRelation(resolved, key));
            }
        }

        if (metadata is null
            || !metadata.TryGetValue("Content.ExportXml", out var exportXml)
            || string.IsNullOrWhiteSpace(exportXml))
        {
            return Deduplicate(relations);
        }

        foreach (var callInfo in ParseCallInfo(exportXml))
        {
            var resolvedTarget = ResolveTarget(callInfo.TargetName, instanceTargetMap);

            if (!string.IsNullOrWhiteSpace(resolvedTarget))
            {
                relations.Add(new ExtractedCallRelation(resolvedTarget, "CallInfo"));
            }

            if (!string.IsNullOrWhiteSpace(callInfo.InstanceName))
            {
                var instanceResolved = ResolveTarget(callInfo.InstanceName!, instanceTargetMap);

                if (!string.IsNullOrWhiteSpace(resolvedTarget)
                    && (string.IsNullOrWhiteSpace(instanceResolved)
                        || string.Equals(instanceResolved, RelationshipTargetResolver.NormalizeTarget(callInfo.InstanceName!), StringComparison.OrdinalIgnoreCase)))
                {
                    instanceResolved = resolvedTarget;
                }

                if (!string.IsNullOrWhiteSpace(instanceResolved))
                {
                    relations.Add(new ExtractedCallRelation(instanceResolved, $"CallInstance:{callInfo.InstanceName}"));
                }
            }
        }

        return Deduplicate(relations);
    }

    private static IReadOnlyList<ExtractedCallRelation> Deduplicate(IReadOnlyList<ExtractedCallRelation> relations)
    {
        return relations
            .DistinctBy(relation => $"{relation.Target}|{relation.MetadataKey}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveTarget(string? rawTarget, IReadOnlyDictionary<string, string> instanceTargetMap)
    {
        var normalized = RelationshipTargetResolver.NormalizeTarget(rawTarget ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return instanceTargetMap.TryGetValue(normalized, out var mapped)
            ? mapped
            : normalized;
    }

    private static IEnumerable<CallInfoRecord> ParseCallInfo(string exportXml)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(exportXml, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            yield break;
        }

        var callInfos = document
            .Descendants()
            .Where(element => element.Name.LocalName.Equals("CallInfo", StringComparison.OrdinalIgnoreCase));

        foreach (var callInfo in callInfos)
        {
            var target = callInfo.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            var instanceName = callInfo
                .Descendants()
                .Where(element => element.Name.LocalName.Equals("Component", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (string.IsNullOrWhiteSpace(target) && string.IsNullOrWhiteSpace(instanceName))
            {
                continue;
            }

            yield return new CallInfoRecord(target, instanceName);
        }
    }

    private static string ReadFirstNonEmpty(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            return value;
        }

        return string.Empty;
    }

    internal sealed record ExtractedCallRelation(string Target, string MetadataKey);

    private sealed record CallInfoRecord(string? TargetName, string? InstanceName);
}
