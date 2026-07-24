namespace TiaProjectExporter.Application.Abstractions;

/// <summary>
/// A resilient unit of export work.
/// </summary>
public interface IExportStage
{
    /// <summary>
    /// Gets the unique stage name used in logs and reports.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the stage.
    /// </summary>
    Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken);
}

