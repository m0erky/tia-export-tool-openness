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

        context.AddResult(new ExportedObjectResult(
            "InventoryObjects",
            "Inventory Object Bundles",
            ExportObjectStatus.Succeeded,
            $"Exported {inventory.Objects.Count} objects into {exportedBundles} bundles"));

        await context.ReportProgressAsync(
            new ExportProgressUpdate(Name, $"Exported {inventory.Objects.Count} objects into {exportedBundles} bundles", inventory.Objects.Count, inventory.Objects.Count, TimeSpan.Zero)).ConfigureAwait(false);
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

    private sealed record BundleEntry(
        string Name,
        string QualifiedPath,
        int Depth,
        IReadOnlyDictionary<string, string> Metadata,
        string? ExportXmlContent,
        string? SourceTextContent);
}
