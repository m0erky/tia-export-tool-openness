using System.Text;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Generates the block call graph markdown from discovered inventory metadata.
/// </summary>
public sealed class BlockCallGraphStage : IExportStage
{
    /// <inheritdoc />
    public string Name => "Block Call Graph";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Options.GenerateMarkdownSummaries || !context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            return;
        }

        var inventory = context.Inventory;
        var markdown = BuildCallGraphMarkdown(inventory);

        await context.WriteArtifactAsync(
            new ExportArtifact("Export/BLOCK_CALL_GRAPH.md", ExportFormat.Markdown, markdown),
            cancellationToken).ConfigureAwait(false);

        context.AddResult(new ExportedObjectResult("Analysis", "BlockCallGraph", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Call graph generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static string BuildCallGraphMarkdown(TiaProjectInventory? inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Block Call Graph");
        builder.AppendLine();

        if (inventory is null)
        {
            builder.AppendLine("No project inventory is available for call graph generation.");
            return builder.ToString();
        }

        var blocks = inventory.Objects
            .Where(node => IsBlockNode(node.ObjectType))
            .Select(node => new BlockNode(node.Name, node.ObjectType, node.QualifiedPath, ParseCalls(node.Metadata)))
            .ToArray();

        builder.AppendLine($"Inventory status: **{inventory.Status}**");
        builder.AppendLine();
        builder.AppendLine($"Discovered block-like objects: **{blocks.Length}**");
        builder.AppendLine();

        if (blocks.Length == 0)
        {
            builder.AppendLine("No block objects were discovered yet. Once PLC block traversal is expanded, this graph will include OB/FB/FC/DB call relationships.");
            return builder.ToString();
        }

        var edges = blocks
            .SelectMany(block => block.Calls.Select(target => (Source: block.Name, Target: target)))
            .Distinct()
            .OrderBy(edge => edge.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.Target, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        builder.AppendLine("## Mermaid");
        builder.AppendLine();
        builder.AppendLine("```mermaid");
        builder.AppendLine("graph TD");

        if (edges.Length == 0)
        {
            foreach (var block in blocks.Take(60))
            {
                builder.AppendLine($"    {NormalizeNodeId(block.Name)}[{EscapeLabel(block.Name)}]");
            }
        }
        else
        {
            foreach (var edge in edges)
            {
                builder.AppendLine($"    {NormalizeNodeId(edge.Source)}[{EscapeLabel(edge.Source)}] --> {NormalizeNodeId(edge.Target)}[{EscapeLabel(edge.Target)}]");
            }
        }

        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Blocks");
        builder.AppendLine();

        foreach (var block in blocks.OrderBy(block => block.Name, StringComparer.OrdinalIgnoreCase).Take(120))
        {
            builder.AppendLine($"- {block.ObjectType}: `{block.QualifiedPath}`");

            if (block.Calls.Count > 0)
            {
                builder.AppendLine($"  - Calls: {string.Join(", ", block.Calls.OrderBy(call => call, StringComparer.OrdinalIgnoreCase))}");
            }
        }

        return builder.ToString();
    }

    private static bool IsBlockNode(string objectType) =>
        objectType.Contains("OB", StringComparison.OrdinalIgnoreCase)
        || objectType.Contains("FB", StringComparison.OrdinalIgnoreCase)
        || objectType.Contains("FC", StringComparison.OrdinalIgnoreCase)
        || objectType.Contains("DB", StringComparison.OrdinalIgnoreCase)
        || objectType.Contains("Block", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ParseCalls(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return Array.Empty<string>();
        }

        if (!metadata.TryGetValue("Calls", out var callsRaw) || string.IsNullOrWhiteSpace(callsRaw))
        {
            return Array.Empty<string>();
        }

        return callsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeNodeId(string value)
    {
        var characters = value.Where(character => char.IsLetterOrDigit(character) || character == '_').ToArray();
        var normalized = new string(characters);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "node";
        }

        if (char.IsDigit(normalized[0]))
        {
            normalized = $"n_{normalized}";
        }

        return normalized;
    }

    private static string EscapeLabel(string value) => value.Replace('"', '\'');

    private sealed record BlockNode(
        string Name,
        string ObjectType,
        string QualifiedPath,
        IReadOnlyList<string> Calls);
}
