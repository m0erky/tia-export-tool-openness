using Microsoft.Extensions.Logging;
using System.Text;
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
    private readonly List<ExportedArtifactInfo> _artifacts = [];
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private TiaProjectInventory? _inventory;
    private ExportArchiveInfo? _archiveInfo;

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
    /// Gets the accumulated export results.
    /// </summary>
    public IReadOnlyList<ExportedObjectResult> Results => _results;

    /// <summary>
    /// Gets the accumulated recoverable issues.
    /// </summary>
    public IReadOnlyList<ExportIssue> Issues => _issues;

    /// <summary>
    /// Gets metadata for artifacts written during this run.
    /// </summary>
    public IReadOnlyList<ExportedArtifactInfo> Artifacts => _artifacts;

    /// <summary>
    /// Gets directories ensured during this run.
    /// </summary>
    public IReadOnlyCollection<string> Directories => _directories;

    /// <summary>
    /// Gets the latest inventory snapshot produced during the run.
    /// </summary>
    public TiaProjectInventory? Inventory => _inventory;

    /// <summary>
    /// Gets the generated archive metadata when compression is executed.
    /// </summary>
    public ExportArchiveInfo? ArchiveInfo => _archiveInfo;

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
    /// Stores the latest inventory snapshot for downstream stages.
    /// </summary>
    public void SetInventory(TiaProjectInventory inventory) => _inventory = inventory;

    /// <summary>
    /// Stores generated archive metadata for reporting stages.
    /// </summary>
    public void SetArchiveInfo(ExportArchiveInfo archiveInfo) => _archiveInfo = archiveInfo;

    /// <summary>
    /// Ensures a directory and records it in the execution snapshot.
    /// </summary>
    public async Task EnsureDirectoryAsync(string relativePath, CancellationToken cancellationToken)
    {
        await ArtifactWriter.EnsureDirectoryAsync(relativePath, cancellationToken).ConfigureAwait(false);
        _directories.Add(relativePath);
    }

    /// <summary>
    /// Writes an artifact and records metadata for later index/report generation.
    /// </summary>
    public async Task WriteArtifactAsync(ExportArtifact artifact, CancellationToken cancellationToken)
    {
        await ArtifactWriter.WriteArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);

        _artifacts.Add(new ExportedArtifactInfo(
            artifact.RelativePath,
            artifact.Format,
            Encoding.UTF8.GetByteCount(artifact.Content),
            DateTimeOffset.UtcNow));
    }

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
