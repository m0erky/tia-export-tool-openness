using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Extracts multilingual text metadata into centralized artifacts.
/// </summary>
public sealed class MultilingualTextStage : IExportStage
{
    private static readonly string[] CandidateKeys =
    [
        "Text",
        "Comment",
        "Title",
        "Description",
        "DisplayName",
        "Text_de-DE",
        "Text_en-US",
        "Text_fr-FR",
        "Comment_de-DE",
        "Comment_en-US"
    ];

    /// <inheritdoc />
    public string Name => "Multilingual Texts";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var entries = CollectEntries(inventory).ToArray();

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var payload = new
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                inventory.ProjectName,
                Status = inventory.Status.ToString(),
                EntryCount = entries.Length,
                Entries = entries
            };

            var json = JsonSerializer.Serialize(payload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Metadata/MULTILINGUAL_TEXTS.json", ExportFormat.Json, json),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            var markdown = BuildMarkdown(inventory, entries);
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Metadata/MULTILINGUAL_TEXTS.md", ExportFormat.Markdown, markdown),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Metadata", "MultilingualTexts", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Multilingual texts extracted", entries.Length, entries.Length, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static IEnumerable<TextEntry> CollectEntries(TiaProjectInventory inventory)
    {
        foreach (var node in inventory.Objects)
        {
            var metadata = node.Metadata;
            if (metadata is null)
            {
                continue;
            }

            foreach (var (key, value) in metadata)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!IsCandidateKey(key))
                {
                    continue;
                }

                yield return new TextEntry(
                    node.ObjectType,
                    node.Name,
                    node.QualifiedPath,
                    key,
                    InferLanguage(key),
                    value);
            }
        }
    }

    private static bool IsCandidateKey(string key) =>
        CandidateKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
        || key.StartsWith("Text_", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Comment_", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Description_", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("_de-DE", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("_en-US", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("_fr-FR", StringComparison.OrdinalIgnoreCase);

    private static string InferLanguage(string key)
    {
        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var language = parts.LastOrDefault();

        if (language is null)
        {
            return "neutral";
        }

        if (language.Contains('-', StringComparison.Ordinal))
        {
            return language;
        }

        return "neutral";
    }

    private static string BuildMarkdown(TiaProjectInventory inventory, IReadOnlyList<TextEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Multilingual Texts");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Entries: **{entries.Count}**");
        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("No multilingual or textual metadata entries were found in the current inventory snapshot.");
            return builder.ToString();
        }

        builder.AppendLine("## Sample Entries");
        builder.AppendLine();

        foreach (var entry in entries.Take(120))
        {
            builder.AppendLine($"- {entry.ObjectType}/{entry.Name} `{entry.QualifiedPath}`");
            builder.AppendLine($"  - {entry.Key} ({entry.Language}): {entry.Value}");
        }

        return builder.ToString();
    }

    private sealed record TextEntry(
        string ObjectType,
        string Name,
        string QualifiedPath,
        string Key,
        string Language,
        string Value);
}
