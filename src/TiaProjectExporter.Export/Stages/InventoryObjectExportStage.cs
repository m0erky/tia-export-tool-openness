using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Exports discovered inventory objects as compact domain/type bundles with deep content.
/// </summary>
public sealed class InventoryObjectExportStage : IExportStage
{
    /// <inheritdoc />
    public string Name => "Inventory Object Export";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            context.AddResult(new ExportedObjectResult("InventoryObjects", "Inventory not available", ExportObjectStatus.Skipped, "No inventory in context"));
            return;
        }

        var grouped = inventory.Objects
            .Where(node => IsDomainIncluded(context.Options, node))
            .GroupBy(node => new BundleKey(TiaInventoryDomainClassifier.ToFolderName(TiaInventoryDomainClassifier.ResolveDomain(node)), NormalizeObjectType(node.ObjectType)))
            .OrderBy(group => group.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.ObjectType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var jsonOptions = JsonOptionsFactory.CreateDefault();
        var exportedBundles = 0;
        var byNameSummary = BlockByNameExportSummary.Empty;

        foreach (var group in grouped)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entries = group
                .OrderBy(node => node.QualifiedPath, StringComparer.OrdinalIgnoreCase)
                .Select(BuildBundleEntry)
                .ToArray();

            var relativeBasePath = $"Export/{group.Key.Domain}/Bundles/{SanitizePathSegment(group.Key.ObjectType)}";

            if (context.Options.Formats.Contains(ExportFormat.Json))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    domain = group.Key.Domain,
                    objectType = group.Key.ObjectType,
                    totalObjects = entries.Length,
                    objects = entries
                }, jsonOptions);

                await context.WriteArtifactAsync(
                    new ExportArtifact($"{relativeBasePath}.json", ExportFormat.Json, payload),
                    cancellationToken).ConfigureAwait(false);
            }

            if (context.Options.Formats.Contains(ExportFormat.Xml))
            {
                var xml = BuildBundleXml(group.Key, entries);
                await context.WriteArtifactAsync(
                    new ExportArtifact($"{relativeBasePath}.xml", ExportFormat.Xml, xml),
                    cancellationToken).ConfigureAwait(false);
            }

            if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
            {
                var markdown = BuildBundleMarkdown(group.Key, entries);
                await context.WriteArtifactAsync(
                    new ExportArtifact($"{relativeBasePath}.md", ExportFormat.Markdown, markdown),
                    cancellationToken).ConfigureAwait(false);
            }

            exportedBundles++;
        }

        var blockNodes = inventory.Objects
            .Where(node => IsDomainIncluded(context.Options, node))
            .Where(node => TiaInventoryDomainClassifier.ResolveDomain(node) == ExportDomain.Blocks)
            .ToArray();

        if (blockNodes.Length > 0)
        {
            byNameSummary = await WritePerBlockArtifactsAsync(context, blockNodes, cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult(
            "InventoryObjects",
            "Inventory Object Bundles",
            ExportObjectStatus.Succeeded,
            $"Exported {inventory.Objects.Count} objects into {exportedBundles} bundles and {byNameSummary.ExportedBlocks} per-block files"));

        if (byNameSummary.ExportedBlocks > 0)
        {
            context.AddResult(new ExportedObjectResult(
                "StructuredTextReconstruction",
                "ByName Blocks",
                ExportObjectStatus.Succeeded,
                $"Blocks with exportXml: {byNameSummary.BlocksWithExportXml}; Success: {byNameSummary.Success}; NoStructuredText: {byNameSummary.NoStructuredText}; ParseError: {byNameSummary.ParseError}; UnsupportedPattern: {byNameSummary.UnsupportedPattern}"));
        }

        await context.ReportProgressAsync(
            new ExportProgressUpdate(Name, $"Exported {inventory.Objects.Count} objects into {exportedBundles} bundles and {byNameSummary.ExportedBlocks} per-block files", inventory.Objects.Count, inventory.Objects.Count, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static async Task<BlockByNameExportSummary> WritePerBlockArtifactsAsync(
        ExportExecutionContext context,
        IReadOnlyList<TiaProjectObjectNode> blockNodes,
        CancellationToken cancellationToken)
    {
        var index = new List<BlockByNameIndexEntry>(blockNodes.Count);
        var reconstruction = new BlockByNameReconstructionCounter();

        var collisions = blockNodes
            .Select(node => BuildBlockFileBaseName(node))
            .GroupBy(baseName => baseName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var sequenceByBaseName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in blockNodes.OrderBy(entry => entry.QualifiedPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = node.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            metadata.TryGetValue("Content.ExportXml", out var exportXml);
            metadata.TryGetValue("Content.SourceText", out var sourceText);
            metadata.TryGetValue("CanonicalQualifiedPath", out var canonicalPath);
            metadata.TryGetValue("OriginalQualifiedPaths", out var originalPaths);
            metadata.TryGetValue("BlockNumber", out var blockNumber);

            var baseName = BuildBlockFileBaseName(node);
            var finalName = baseName;

            if (collisions.ContainsKey(baseName))
            {
                sequenceByBaseName.TryGetValue(baseName, out var current);
                current++;
                sequenceByBaseName[baseName] = current;
                finalName = $"{baseName}_{current}";
            }

            var relativeBasePath = $"Export/Blocks/ByName/{finalName}";

            StructuredTextReconstructionResult? reconstructionResult = null;
            if (IsStructuredTextTargetBlock(node.ObjectType) && !string.IsNullOrWhiteSpace(exportXml))
            {
                reconstruction.BlocksWithExportXml++;
                reconstructionResult = StructuredTextReconstructor.Reconstruct(exportXml);
                reconstruction.Increment(reconstructionResult.ReconstructionStatus);
            }

            var payload = new
            {
                type = node.ObjectType,
                name = node.Name,
                number = string.IsNullOrWhiteSpace(blockNumber) ? null : blockNumber,
                canonicalPath = string.IsNullOrWhiteSpace(canonicalPath) ? node.QualifiedPath : canonicalPath,
                originalPaths = ParseOriginalPaths(originalPaths, node.QualifiedPath),
                metadata = BuildCompactMetadata(metadata),
                sourceText = string.IsNullOrWhiteSpace(sourceText) ? null : sourceText,
                exportXml = string.IsNullOrWhiteSpace(exportXml) ? null : exportXml,
                reconstructedSourceText = reconstructionResult?.ReconstructedSourceText,
                reconstructionStatus = reconstructionResult?.ReconstructionStatus,
                reconstructionDiagnostics = reconstructionResult?.ReconstructionDiagnostics
            };

            var json = JsonSerializer.Serialize(payload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(new ExportArtifact($"{relativeBasePath}.json", ExportFormat.Json, json), cancellationToken).ConfigureAwait(false);

            var markdown = BuildPerBlockMarkdown(payload.type, payload.name, payload.number, payload.canonicalPath, payload.originalPaths, payload.metadata, payload.sourceText, payload.exportXml);
            await context.WriteArtifactAsync(new ExportArtifact($"{relativeBasePath}.md", ExportFormat.Markdown, markdown), cancellationToken).ConfigureAwait(false);

            if (context.Options.Formats.Contains(ExportFormat.Xml))
            {
                var xml = BuildPerBlockXml(payload.type, payload.name, payload.number, payload.canonicalPath, payload.originalPaths, payload.metadata, payload.sourceText, payload.exportXml);
                await context.WriteArtifactAsync(new ExportArtifact($"{relativeBasePath}.xml", ExportFormat.Xml, xml), cancellationToken).ConfigureAwait(false);
            }

            index.Add(new BlockByNameIndexEntry(
                payload.type,
                payload.name,
                payload.number,
                $"{finalName}.json",
                payload.canonicalPath));
        }

        var indexJson = JsonSerializer.Serialize(index, JsonOptionsFactory.CreateDefault());
        await context.WriteArtifactAsync(new ExportArtifact("Export/Blocks/ByName/INDEX.json", ExportFormat.Json, indexJson), cancellationToken).ConfigureAwait(false);

        return new BlockByNameExportSummary(
            ExportedBlocks: blockNodes.Count,
            BlocksWithExportXml: reconstruction.BlocksWithExportXml,
            Success: reconstruction.Success,
            NoStructuredText: reconstruction.NoStructuredText,
            ParseError: reconstruction.ParseError,
            UnsupportedPattern: reconstruction.UnsupportedPattern);
    }

    private static bool IsStructuredTextTargetBlock(string objectType) =>
        string.Equals(objectType, "FB", StringComparison.OrdinalIgnoreCase)
        || string.Equals(objectType, "FC", StringComparison.OrdinalIgnoreCase)
        || string.Equals(objectType, "OB", StringComparison.OrdinalIgnoreCase);

    private static string BuildBlockFileBaseName(TiaProjectObjectNode node)
    {
        var normalizedType = SanitizePathSegment(NormalizeObjectType(node.ObjectType));
        var normalizedName = SanitizePathSegment(string.IsNullOrWhiteSpace(node.Name) ? "Unnamed" : node.Name);
        return $"{normalizedType}_{normalizedName}";
    }

    private static IReadOnlyList<string> ParseOriginalPaths(string? rawPaths, string fallbackPath)
    {
        if (string.IsNullOrWhiteSpace(rawPaths))
        {
            return new[] { fallbackPath };
        }

        return rawPaths
            .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildPerBlockMarkdown(
        string objectType,
        string name,
        string? number,
        string canonicalPath,
        IReadOnlyList<string> originalPaths,
        IReadOnlyDictionary<string, string> metadata,
        string? sourceText,
        string? exportXml)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {objectType} {name}");
        builder.AppendLine();
        builder.AppendLine($"- Type: **{objectType}**");
        builder.AppendLine($"- Name: **{name}**");
        builder.AppendLine($"- Number: **{number ?? "n/a"}**");
        builder.AppendLine($"- Canonical path: `{canonicalPath}`");
        builder.AppendLine();
        builder.AppendLine("## Original Paths");
        builder.AppendLine();

        foreach (var path in originalPaths)
        {
            builder.AppendLine($"- `{path}`");
        }

        if (metadata.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Metadata");
            builder.AppendLine();

            foreach (var pair in metadata.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- {pair.Key}: `{pair.Value}`");
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            builder.AppendLine();
            builder.AppendLine("## Source Text");
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(sourceText);
            builder.AppendLine("```");
        }

        if (!string.IsNullOrWhiteSpace(exportXml))
        {
            builder.AppendLine();
            builder.AppendLine("## Export XML");
            builder.AppendLine();
            builder.AppendLine("```xml");
            builder.AppendLine(exportXml);
            builder.AppendLine("```");
        }

        return builder.ToString();
    }

    private static string BuildPerBlockXml(
        string objectType,
        string name,
        string? number,
        string canonicalPath,
        IReadOnlyList<string> originalPaths,
        IReadOnlyDictionary<string, string> metadata,
        string? sourceText,
        string? exportXml)
    {
        var document = new XDocument(
            new XElement(
                "BlockExport",
                new XAttribute("type", objectType),
                new XAttribute("name", name),
                new XAttribute("number", number ?? string.Empty),
                new XElement("CanonicalPath", canonicalPath),
                new XElement("OriginalPaths", originalPaths.Select(path => new XElement("Path", path))),
                new XElement("Metadata", metadata.Select(pair => new XElement("Entry", new XAttribute("key", pair.Key), pair.Value))),
                string.IsNullOrWhiteSpace(sourceText) ? null : new XElement("SourceText", new XCData(sourceText)),
                string.IsNullOrWhiteSpace(exportXml) ? null : new XElement("ExportXml", new XCData(exportXml))));

        return document.ToString();
    }

    private static BundleEntry BuildBundleEntry(TiaProjectObjectNode node)
    {
        var metadata = node.Metadata ?? new Dictionary<string, string>();

        metadata.TryGetValue("Content.ExportXml", out var exportXml);
        metadata.TryGetValue("Content.SourceText", out var sourceText);

        return new BundleEntry(
            Name: node.Name,
            QualifiedPath: node.QualifiedPath,
            Depth: node.Depth,
            Metadata: BuildCompactMetadata(metadata),
            ExportXmlContent: string.IsNullOrWhiteSpace(exportXml) ? null : exportXml,
            SourceTextContent: string.IsNullOrWhiteSpace(sourceText) ? null : sourceText);
    }

    private static string BuildBundleXml(BundleKey key, IReadOnlyCollection<BundleEntry> entries)
    {
        var document = new XDocument(
            new XElement(
                "TiaObjectBundle",
                new XAttribute("domain", key.Domain),
                new XAttribute("objectType", key.ObjectType),
                new XAttribute("count", entries.Count),
                entries.Select(entry =>
                    new XElement(
                        "Object",
                        new XAttribute("name", entry.Name),
                        new XAttribute("path", entry.QualifiedPath),
                        new XAttribute("depth", entry.Depth),
                        new XElement("Metadata", entry.Metadata.Select(pair => new XElement("Entry", new XAttribute("key", pair.Key), pair.Value))),
                        string.IsNullOrWhiteSpace(entry.ExportXmlContent)
                            ? null
                            : new XElement("ExportXml", new XCData(entry.ExportXmlContent)),
                        string.IsNullOrWhiteSpace(entry.SourceTextContent)
                            ? null
                            : new XElement("SourceText", new XCData(entry.SourceTextContent))))));

        return document.ToString();
    }

    private static string BuildBundleMarkdown(BundleKey key, IReadOnlyCollection<BundleEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {key.Domain} / {key.ObjectType} Bundle");
        builder.AppendLine();
        builder.AppendLine($"Objects: **{entries.Count}**");
        builder.AppendLine();

        builder.AppendLine("## Objects");
        builder.AppendLine();

        foreach (var entry in entries)
        {
            builder.AppendLine($"- `{entry.QualifiedPath}`");
        }

        var withSource = entries.Where(item => !string.IsNullOrWhiteSpace(item.SourceTextContent)).ToArray();
        if (withSource.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Source Content");
            builder.AppendLine();

            foreach (var entry in withSource)
            {
                builder.AppendLine($"### {entry.Name}");
                builder.AppendLine();
                builder.AppendLine($"Path: `{entry.QualifiedPath}`");
                builder.AppendLine();
                builder.AppendLine("```text");
                builder.AppendLine(entry.SourceTextContent);
                builder.AppendLine("```");
                builder.AppendLine();
            }
        }

        var withExportXml = entries.Where(item => !string.IsNullOrWhiteSpace(item.ExportXmlContent)).ToArray();
        if (withExportXml.Length > 0)
        {
            builder.AppendLine("## Export XML Content");
            builder.AppendLine();

            foreach (var entry in withExportXml)
            {
                builder.AppendLine($"### {entry.Name}");
                builder.AppendLine();
                builder.AppendLine($"Path: `{entry.QualifiedPath}`");
                builder.AppendLine();
                builder.AppendLine("```xml");
                builder.AppendLine(entry.ExportXmlContent);
                builder.AppendLine("```");
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> BuildCompactMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        var source = metadata ?? new Dictionary<string, string>();
        var compact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in source)
        {
            if (pair.Key.StartsWith("Prop.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pair.Key.StartsWith("Content.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            compact[pair.Key] = pair.Value;
        }

        return compact;
    }

    private static bool IsDomainIncluded(ExportOptions options, TiaProjectObjectNode node)
    {
        var includedDomains = options.IncludedDomains;

        if (includedDomains is null || includedDomains.Count == 0)
        {
            return true;
        }

        var domain = TiaInventoryDomainClassifier.ResolveDomain(node);
        return includedDomains.Contains(domain);
    }

    private static string NormalizeObjectType(string objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType))
        {
            return "Unmapped";
        }

        return objectType.Trim();
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "item";
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
            }
        }

        var normalized = builder.ToString().Trim('_', '.', ' ');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "item";
        }

        return normalized.Length > 80
            ? normalized.Substring(0, 80)
            : normalized;
    }

    private readonly record struct BundleKey(string Domain, string ObjectType);

    private sealed record BlockByNameIndexEntry(
        string Type,
        string Name,
        string? Number,
        string File,
        string CanonicalPath);

    private sealed record BundleEntry(
        string Name,
        string QualifiedPath,
        int Depth,
        IReadOnlyDictionary<string, string> Metadata,
        string? ExportXmlContent,
        string? SourceTextContent);

    private sealed record BlockByNameExportSummary(
        int ExportedBlocks,
        int BlocksWithExportXml,
        int Success,
        int NoStructuredText,
        int ParseError,
        int UnsupportedPattern)
    {
        public static BlockByNameExportSummary Empty { get; } = new(0, 0, 0, 0, 0, 0);
    }

    private sealed class BlockByNameReconstructionCounter
    {
        public int BlocksWithExportXml { get; set; }

        public int Success { get; set; }

        public int NoStructuredText { get; set; }

        public int ParseError { get; set; }

        public int UnsupportedPattern { get; set; }

        public void Increment(string status)
        {
            if (string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                Success++;
                return;
            }

            if (string.Equals(status, "NoStructuredText", StringComparison.OrdinalIgnoreCase))
            {
                NoStructuredText++;
                return;
            }

            if (string.Equals(status, "ParseError", StringComparison.OrdinalIgnoreCase))
            {
                ParseError++;
                return;
            }

            if (string.Equals(status, "UnsupportedPattern", StringComparison.OrdinalIgnoreCase))
            {
                UnsupportedPattern++;
            }
        }
    }
}
