using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Writes the current TIA project inventory status into the export repository.
/// </summary>
public sealed class ProjectInventoryStage : IExportStage
{
    private readonly ITiaProjectInventoryProvider _inventoryProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectInventoryStage"/> class.
    /// </summary>
    public ProjectInventoryStage(ITiaProjectInventoryProvider inventoryProvider)
    {
        _inventoryProvider = inventoryProvider;
    }

    /// <inheritdoc />
    public string Name => "TIA Project Inventory";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = await _inventoryProvider.BuildInventoryAsync(context.Options.ProjectPath, cancellationToken).ConfigureAwait(false);
        var jsonOptions = JsonOptionsFactory.CreateDefault();

        var inventoryJson = JsonSerializer.Serialize(inventory, jsonOptions);
        var inventoryXml = BuildInventoryXml(inventory);
        var summaryMarkdown = BuildSummaryMarkdown(inventory);

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            await context.ArtifactWriter.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/TIA_PROJECT_INVENTORY.json", ExportFormat.Json, inventoryJson),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.Formats.Contains(ExportFormat.Xml))
        {
            await context.ArtifactWriter.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/TIA_PROJECT_INVENTORY.xml", ExportFormat.Xml, inventoryXml),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.ArtifactWriter.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/TIA_PROJECT_INVENTORY.md", ExportFormat.Markdown, summaryMarkdown),
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var issue in inventory.Issues)
        {
            context.AddIssue(issue);
        }

        var status = inventory.Status switch
        {
            TiaInventoryStatus.Complete => ExportObjectStatus.Succeeded,
            TiaInventoryStatus.Partial => ExportObjectStatus.Skipped,
            _ => ExportObjectStatus.Skipped
        };

        context.AddResult(new ExportedObjectResult("Inventory", inventory.ProjectName ?? "TIA project", status, inventory.Status.ToString()));
        await context.ReportProgressAsync(
            new ExportProgressUpdate(Name, inventory.ProjectName ?? "Inventory exported", inventory.Objects.Count, inventory.Objects.Count, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static string BuildSummaryMarkdown(TiaProjectInventory inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# TIA Project Inventory");
        builder.AppendLine();
        builder.AppendLine($"Status: **{inventory.Status}**");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Path: `{inventory.ProjectPath ?? "Not configured"}`");
        builder.AppendLine();
        builder.AppendLine($"Discovered objects: **{inventory.Objects.Count}**");
        builder.AppendLine();

        if (inventory.Issues.Count > 0)
        {
            builder.AppendLine("## Issues");
            builder.AppendLine();

            foreach (var issue in inventory.Issues)
            {
                builder.AppendLine($"- {issue.Scope}: {issue.Message}");
            }

            builder.AppendLine();
        }

        if (inventory.Objects.Count > 0)
        {
            builder.AppendLine("## Top Objects");
            builder.AppendLine();

            foreach (var node in inventory.Objects.Take(25))
            {
                builder.AppendLine($"- {node.ObjectType}: `{node.QualifiedPath}`");
            }
        }

        return builder.ToString();
    }

    private static string BuildInventoryXml(TiaProjectInventory inventory)
    {
        var document = new XDocument(
            new XElement(
                "TiaProjectInventory",
                new XAttribute("status", inventory.Status),
                new XElement("ProjectName", inventory.ProjectName ?? string.Empty),
                new XElement("ProjectPath", inventory.ProjectPath ?? string.Empty),
                new XElement(
                    "Issues",
                    inventory.Issues.Select(issue =>
                        new XElement(
                            "Issue",
                            new XAttribute("scope", issue.Scope),
                            new XElement("Message", issue.Message),
                            new XElement("Details", issue.Details ?? string.Empty)))),
                new XElement(
                    "Objects",
                    inventory.Objects.Select(node =>
                        new XElement(
                            "Object",
                            new XAttribute("type", node.ObjectType),
                            new XAttribute("depth", node.Depth),
                            new XElement("Name", node.Name),
                            new XElement("QualifiedPath", node.QualifiedPath),
                            new XElement(
                                "Metadata",
                                (node.Metadata ?? new Dictionary<string, string>()).Select(pair =>
                                    new XElement("Entry", new XAttribute("key", pair.Key), pair.Value))))))));

        return document.ToString();
    }
}
