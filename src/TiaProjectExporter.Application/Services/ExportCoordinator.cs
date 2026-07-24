using Microsoft.Extensions.Logging;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Application.Services;

/// <summary>
/// Coordinates export stages and guarantees resilient execution.
/// </summary>
public sealed class ExportCoordinator
{
    private readonly IReadOnlyList<IExportStage> _stages;
    private readonly IExportArtifactWriterFactory _artifactWriterFactory;
    private readonly ILogger<ExportCoordinator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportCoordinator"/> class.
    /// </summary>
    public ExportCoordinator(
        IEnumerable<IExportStage> stages,
        IExportArtifactWriterFactory artifactWriterFactory,
        ILogger<ExportCoordinator> logger)
    {
        _stages = stages.ToArray();
        _artifactWriterFactory = artifactWriterFactory;
        _logger = logger;
    }

    /// <summary>
    /// Executes all registered export stages and returns a report.
    /// </summary>
    public async Task<ExportReport> ExecuteAsync(
        ExportOptions options,
        Func<ExportProgressUpdate, Task>? progressCallback,
        CancellationToken cancellationToken)
    {
        var artifactWriter = _artifactWriterFactory.Create(options.OutputDirectory);
        var context = new ExportExecutionContext(options, artifactWriter, _logger, progressCallback);

        foreach (var stage in _stages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation("Starting export stage {StageName}", stage.Name);
                await context.ReportProgressAsync(new ExportProgressUpdate(stage.Name, "Starting", 0, null, null)).ConfigureAwait(false);
                await stage.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                context.AddResult(new ExportedObjectResult("Stage", stage.Name, ExportObjectStatus.Succeeded));
                _logger.LogInformation("Completed export stage {StageName}", stage.Name);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Export cancelled during stage {StageName}", stage.Name);
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Stage {StageName} failed but export will continue", stage.Name);
                context.AddIssue(new ExportIssue(stage.Name, exception.Message, exception.ToString()));
                context.AddResult(new ExportedObjectResult("Stage", stage.Name, ExportObjectStatus.Failed, exception.Message));
            }
        }

        return context.BuildReport();
    }
}
