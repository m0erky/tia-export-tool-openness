using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory;

/// <summary>
/// Placeholder Openness adapter used until Siemens assemblies are connected.
/// </summary>
public sealed class UnavailableTiaProjectOpennessAdapter : ITiaProjectOpennessAdapter
{
    /// <inheritdoc />
    public Task<TiaProjectTraversalResult> TraverseAsync(string projectPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var issues = new[]
        {
            new ExportIssue(
                "OpennessAdapter",
                "TIA project traversal is not connected in the current milestone.",
                "Add Siemens.Engineering-backed traversal in the next milestone to enumerate devices, software, and metadata.")
        };

        var result = new TiaProjectTraversalResult(
            ProjectName: Path.GetFileNameWithoutExtension(projectPath),
            ProjectPath: projectPath,
            Objects: Array.Empty<TiaProjectObjectNode>(),
            Issues: issues);

        return Task.FromResult(result);
    }
}
