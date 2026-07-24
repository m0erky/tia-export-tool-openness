using System.Security.Cryptography;
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

        var archiveInfo = await BuildArchiveInfoAsync(archivePath, cancellationToken).ConfigureAwait(false);
        context.SetArchiveInfo(archiveInfo);

        context.AddResult(new ExportedObjectResult("Packaging", "ExportZip", ExportObjectStatus.Succeeded, archivePath));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Export.zip generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static async Task<ExportArchiveInfo> BuildArchiveInfoAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(archivePath))
        {
            return new ExportArchiveInfo(archivePath, SizeBytes: null, Sha256: null, DateTimeOffset.UtcNow);
        }

        var fileInfo = new FileInfo(archivePath);

        await using var stream = File.OpenRead(archivePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var sha256 = Convert.ToHexString(hash);

        return new ExportArchiveInfo(archivePath, fileInfo.Length, sha256, DateTimeOffset.UtcNow);
    }
}
