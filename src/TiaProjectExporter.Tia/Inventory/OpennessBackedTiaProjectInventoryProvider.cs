using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory;

/// <summary>
/// Builds project inventory data from a Siemens Openness traversal adapter.
/// </summary>
public sealed class OpennessBackedTiaProjectInventoryProvider : ITiaProjectInventoryProvider
{
    private readonly ITiaProjectOpennessAdapter _opennessAdapter;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpennessBackedTiaProjectInventoryProvider"/> class.
    /// </summary>
    public OpennessBackedTiaProjectInventoryProvider(ITiaProjectOpennessAdapter opennessAdapter)
    {
        _opennessAdapter = opennessAdapter;
    }

    /// <inheritdoc />
    public async Task<TiaProjectInventory> BuildInventoryAsync(string? projectPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return new TiaProjectInventory(
                TiaInventoryStatus.Unavailable,
                ProjectName: null,
                ProjectPath: null,
                Objects: Array.Empty<TiaProjectObjectNode>(),
                Issues:
                [
                    new ExportIssue(
                        "ProjectSelection",
                        "No TIA project path is configured yet.",
                        "Select a project path in the UI before running an export.")
                ]);
        }

        TiaProjectTraversalResult traversal;

        try
        {
            traversal = await _opennessAdapter.TraverseAsync(projectPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new TiaProjectInventory(
                TiaInventoryStatus.Unavailable,
                ProjectName: Path.GetFileNameWithoutExtension(projectPath),
                ProjectPath: projectPath,
                Objects: Array.Empty<TiaProjectObjectNode>(),
                Issues:
                [
                    new ExportIssue(
                        "OpennessTraversal",
                        "TIA Openness traversal failed before inventory could be produced.",
                        exception.Message)
                ]);
        }

        var status = DetermineStatus(traversal.Objects.Count, traversal.Issues.Count);

        return new TiaProjectInventory(
            status,
            traversal.ProjectName,
            traversal.ProjectPath,
            traversal.Objects,
            traversal.Issues);
    }

    private static TiaInventoryStatus DetermineStatus(int objectCount, int issueCount)
    {
        if (objectCount == 0 && issueCount > 0)
        {
            return TiaInventoryStatus.Unavailable;
        }

        if (issueCount > 0)
        {
            return TiaInventoryStatus.Partial;
        }

        return objectCount > 0 ? TiaInventoryStatus.Complete : TiaInventoryStatus.Unavailable;
    }
}
