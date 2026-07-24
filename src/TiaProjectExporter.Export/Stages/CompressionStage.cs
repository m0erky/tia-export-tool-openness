using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Creates a ZIP package for the generated export repository when enabled.
/// </summary>
public sealed class CompressionStage : IExportStage
{
    private readonly IExportArchiveService _archiveService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionStage"/> class.
    /// </summary>
    public CompressionStage(IExportArchiveService archiveService)
    {
        _archiveService = archiveService;
    }

    /// <inheritdoc />
    public string Name => "Compression";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Options.EnableCompression)
        {
            context.AddResult(new ExportedObjectResult("Packaging", "ExportZip", ExportObjectStatus.Skipped, "Compression disabled"));
            await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Compression skipped", 0, 0, TimeSpan.Zero)).ConfigureAwait(false);
            return;
        }

        var archivePath = await _archiveService.CreateArchiveAsync(
            context.Options.OutputDirectory,
            sourceDirectoryName: "Export",
            archiveFileName: "Export.zip",
            cancellationToken).ConfigureAwait(false);

        context.AddResult(new ExportedObjectResult("Packaging", "ExportZip", ExportObjectStatus.Succeeded, archivePath));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Export.zip generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }
}
