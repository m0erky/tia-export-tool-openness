using Microsoft.Extensions.Logging;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Application;

/// <summary>
/// Mutable execution context shared across export stages.
/// </summary>
public sealed class ExportExecutionContext
{
    private readonly List<ExportIssue> _issues = [];
    private readonly List<ExportedObjectResult> _results = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportExecutionContext"/> class.
    /// </summary>
    public ExportExecutionContext(
        ExportOptions options,
        IExportArtifactWriter artifactWriter,
        ILogger logger,
        Func<ExportProgressUpdate, Task>? progressCallback = null)
    {
        Options = options;
        ArtifactWriter = artifactWriter;
        Logger = logger;
        ProgressCallback = progressCallback;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the selected export options.
    /// </summary>
    public ExportOptions Options { get; }

    /// <summary>
    /// Gets the artifact writer for repository output.
    /// </summary>
    public IExportArtifactWriter ArtifactWriter { get; }

    /// <summary>
    /// Gets the logger associated with the export.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Gets the export start time.
    /// </summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Gets the current progress callback.
    /// </summary>
    public Func<ExportProgressUpdate, Task>? ProgressCallback { get; }

    /// <summary>
    /// Adds an export result entry.
    /// </summary>
    public void AddResult(ExportedObjectResult result) => _results.Add(result);

    /// <summary>
    /// Adds a recoverable issue.
    /// </summary>
    public void AddIssue(ExportIssue issue) => _issues.Add(issue);

    /// <summary>
    /// Reports progress to the configured sink.
    /// </summary>
    public Task ReportProgressAsync(ExportProgressUpdate update) =>
        ProgressCallback is null ? Task.CompletedTask : ProgressCallback(update);

    /// <summary>
    /// Builds the final report.
    /// </summary>
    public ExportReport BuildReport() =>
        new(StartedAt, DateTimeOffset.UtcNow, _results.ToArray(), _issues.ToArray());
}

