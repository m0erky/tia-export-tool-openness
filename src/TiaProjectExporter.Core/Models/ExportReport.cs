namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Immutable export summary returned after a run finishes.
/// </summary>
public sealed record ExportReport(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<ExportedObjectResult> Results,
    IReadOnlyList<ExportIssue> Issues)
{
    /// <summary>
    /// Gets the count of successful results.
    /// </summary>
    public int SucceededCount => Results.Count(result => result.Status == ExportObjectStatus.Succeeded);

    /// <summary>
    /// Gets the count of failed results.
    /// </summary>
    public int FailedCount => Results.Count(result => result.Status == ExportObjectStatus.Failed);

    /// <summary>
    /// Gets the count of skipped results.
    /// </summary>
    public int SkippedCount => Results.Count(result => result.Status == ExportObjectStatus.Skipped);
}

