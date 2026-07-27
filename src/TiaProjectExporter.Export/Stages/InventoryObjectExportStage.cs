using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Exports each discovered inventory object into domain folders as JSON/XML/Markdown artifacts.
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

        var jsonOptions = JsonOptionsFactory.CreateDefault();
        var exportedCount = 0;

        for (var index = 0; index < inventory.Objects.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var node = inventory.Objects[index];
            var relativeBasePath = BuildRelativeBasePath(node, index);

            if (context.Options.Formats.Contains(ExportFormat.Json))
            {
                var payload = JsonSerializer.Serialize(BuildSerializableNode(node), jsonOptions);
                await context.WriteArtifactAsync(
                    new ExportArtifact($"{relativeBasePath}.json", ExportFormat.Json, payload),
                    cancellationToken).ConfigureAwait(false);
            }

            if (context.Options.Formats.Contains(ExportFormat.Xml))
            {
                var xml = BuildNodeXml(node);
                await context.WriteArtifactAsync(
                    new ExportArtifact($"{relativeBasePath}.xml", ExportFormat.Xml, xml),
                    cancellationToken).ConfigureAwait(false);
            }

            if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
            {
                var markdown = BuildNodeMarkdown(node);
                await context.WriteArtifactAsync(
                    new ExportArtifact($"{relativeBasePath}.md", ExportFormat.Markdown, markdown),
                    cancellationToken).ConfigureAwait(false);
            }

            await WriteDeepContentArtifactsAsync(context, node, relativeBasePath, cancellationToken).ConfigureAwait(false);

            exportedCount++;
        }

        context.AddResult(new ExportedObjectResult("InventoryObjects", "Inventory Object Files", ExportObjectStatus.Succeeded, $"Exported {exportedCount} objects"));

        await context.ReportProgressAsync(
            new ExportProgressUpdate(Name, $"Exported {exportedCount} inventory objects", exportedCount, exportedCount, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static string BuildRelativeBasePath(TiaProjectObjectNode node, int index)
    {
        var domain = ResolveDomain(node);
        var segments = node.QualifiedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !string.Equals(segment, "Project", StringComparison.OrdinalIgnoreCase))
            .Select(SanitizePathSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Take(6)
            .ToArray();

        if (segments.Length == 0)
        {
            segments = [SanitizePathSegment(node.Name)];
        }

        var folder = string.Join('/', segments);
        var hash = BuildShortHash(node.QualifiedPath);

        return $"Export/{domain}/Objects/{folder}_{index:00000}_{hash}";
    }

    private static string ResolveDomain(TiaProjectObjectNode node)
    {
        if (IsBlockObjectType(node.ObjectType))
        {
            return "Blocks";
        }

        if (IsTagObjectType(node.ObjectType))
        {
            return "Tags";
        }

        if (IsUdtObjectType(node.ObjectType))
        {
            return "UDTs";
        }

        if (IsHmiObjectType(node.ObjectType))
        {
            return "HMI";
        }

        var candidate = $"{node.ObjectType} {node.QualifiedPath} {node.Name}";

        if (ContainsAny(candidate, "Device", "Module", "Rack", "Hardware", "Cpu"))
        {
            return "Hardware";
        }

        if (ContainsAny(candidate, "Hmi", "Screen", "Faceplate", "Recipe", "Alarm"))
        {
            return "HMI";
        }

        if (ContainsAny(candidate, "Tag", "TagTable", "PlcTag"))
        {
            return "Tags";
        }

        if (ContainsAny(candidate, "Udt", "DataType"))
        {
            return "UDTs";
        }

        if (ContainsAny(candidate, "FunctionBlock", "OrganizationBlock", "DataBlock", "InstanceDb", "Block", " FB", " FC", " OB", " DB"))
        {
            return "Blocks";
        }

        if (ContainsAny(candidate, "Network", "Profinet", "Profibus", "Connection", "Subnet", "Port", "Interface"))
        {
            return "Network";
        }

        if (ContainsAny(candidate, "Library"))
        {
            return "Libraries";
        }

        if (ContainsAny(candidate, "Diagnostic", "Audit"))
        {
            return "Diagnostics";
        }

        return "Metadata";
    }

    private static bool IsBlockObjectType(string objectType) =>
        objectType.Equals("OB", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("FB", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("FC", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("DB", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("InstanceDB", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("Block", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("FunctionBlock", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("OrganizationBlock", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("DataBlock", StringComparison.OrdinalIgnoreCase);

    private static bool IsTagObjectType(string objectType) =>
        objectType.Equals("Tag", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("TagTable", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("PlcTag", StringComparison.OrdinalIgnoreCase);

    private static bool IsUdtObjectType(string objectType) =>
        objectType.Equals("UDT", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("DataType", StringComparison.OrdinalIgnoreCase);

    private static bool IsHmiObjectType(string objectType) =>
        objectType.Equals("HMI", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("HmiObject", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("Screen", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("Faceplate", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("Recipe", StringComparison.OrdinalIgnoreCase)
        || objectType.Equals("Alarm", StringComparison.OrdinalIgnoreCase);

    private static object BuildSerializableNode(TiaProjectObjectNode node)
    {
        var compactMetadata = BuildCompactMetadata(node.Metadata);

        return new
        {
            node.ObjectType,
            node.Name,
            node.QualifiedPath,
            node.Depth,
            Metadata = compactMetadata
        };
    }

    private static string BuildNodeXml(TiaProjectObjectNode node)
    {
        var compactMetadata = BuildCompactMetadata(node.Metadata);

        var document = new XDocument(
            new XElement(
                "TiaProjectObject",
                new XAttribute("type", node.ObjectType),
                new XAttribute("depth", node.Depth),
                new XElement("Name", node.Name),
                new XElement("QualifiedPath", node.QualifiedPath),
                new XElement(
                    "Metadata",
                    compactMetadata.Select(pair =>
                        new XElement("Entry", new XAttribute("key", pair.Key), pair.Value)))));

        return document.ToString();
    }

    private static string BuildNodeMarkdown(TiaProjectObjectNode node)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {node.Name}");
        builder.AppendLine();
        builder.AppendLine($"- Type: **{node.ObjectType}**");
        builder.AppendLine($"- Path: `{node.QualifiedPath}`");
        builder.AppendLine($"- Depth: **{node.Depth}**");
        builder.AppendLine();

        var metadata = BuildCompactMetadata(node.Metadata);

        if (metadata.Count == 0)
        {
            builder.AppendLine("No metadata available.");
            return builder.ToString();
        }

        builder.AppendLine("## Metadata");
        builder.AppendLine();

        foreach (var pair in metadata.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {pair.Key}: `{pair.Value}`");
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

            if (pair.Key.StartsWith("Content.", StringComparison.OrdinalIgnoreCase)
                && pair.Key is not "Content.ExportXmlLength" and not "Content.SourceTextLength")
            {
                continue;
            }

            compact[pair.Key] = pair.Value;
        }

        return compact;
    }

    private static async Task WriteDeepContentArtifactsAsync(
        ExportExecutionContext context,
        TiaProjectObjectNode node,
        string relativeBasePath,
        CancellationToken cancellationToken)
    {
        var metadata = node.Metadata ?? new Dictionary<string, string>();

        if (metadata.TryGetValue("Content.ExportXml", out var exportXml)
            && !string.IsNullOrWhiteSpace(exportXml)
            && context.Options.Formats.Contains(ExportFormat.Xml))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact($"{relativeBasePath}.content.export.xml", ExportFormat.Xml, exportXml),
                cancellationToken).ConfigureAwait(false);
        }

        if (metadata.TryGetValue("Content.SourceText", out var sourceText)
            && !string.IsNullOrWhiteSpace(sourceText))
        {
            var format = context.Options.Formats.Contains(ExportFormat.Markdown)
                ? ExportFormat.Markdown
                : ExportFormat.Json;

            var wrappedSource = format == ExportFormat.Markdown
                ? BuildSourceMarkdown(node, sourceText)
                : sourceText;

            var extension = format == ExportFormat.Markdown ? "md" : "txt";
            await context.WriteArtifactAsync(
                new ExportArtifact($"{relativeBasePath}.content.source.{extension}", format, wrappedSource),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildSourceMarkdown(TiaProjectObjectNode node, string sourceText)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Source - {node.Name}");
        builder.AppendLine();
        builder.AppendLine($"Type: **{node.ObjectType}**");
        builder.AppendLine($"Path: `{node.QualifiedPath}`");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(sourceText);
        builder.AppendLine("```");
        return builder.ToString();
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

        return normalized.Length > 64
            ? normalized[..64]
            : normalized;
    }

    private static bool ContainsAny(string candidate, params string[] terms) =>
        terms.Any(term => candidate.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string BuildShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes[..4]).ToLowerInvariant();
    }
}
