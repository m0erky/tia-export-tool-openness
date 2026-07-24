using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory;

/// <summary>
/// Placeholder inventory provider used until the Siemens Openness traversal is connected.
/// </summary>
public sealed class UnavailableTiaProjectInventoryProvider : ITiaProjectInventoryProvider
{
    /// <inheritdoc />
    public Task<TiaProjectInventory> BuildInventoryAsync(string? projectPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var issues = new List<ExportIssue>();

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            issues.Add(new ExportIssue(
                "ProjectSelection",
                "No TIA project path is configured yet.",
                "Select a project path in the UI before running a real inventory export."));
        }

        issues.Add(new ExportIssue(
            "OpennessAdapter",
            "TIA project traversal is not connected in the current milestone.",
            "Milestone 2 defines the inventory contract and placeholder output. A concrete Siemens Openness adapter is the next implementation step."));

        var inventory = new TiaProjectInventory(
            TiaInventoryStatus.Unavailable,
            ProjectName: projectPath is null ? null : Path.GetFileNameWithoutExtension(projectPath),
            ProjectPath: projectPath,
            Objects: Array.Empty<TiaProjectObjectNode>(),
            Issues: issues);

        return Task.FromResult(inventory);
    }
}

