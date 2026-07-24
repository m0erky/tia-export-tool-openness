using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class CompressionStageTests
{
    [Fact]
    public async Task ExecuteAsync_CreatesArchive_WhenCompressionEnabled()
    {
        var archiveService = new StubArchiveService("/tmp/out/Export.zip");
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out") with { EnableCompression = true },
            new RecordingArtifactWriter(),
            NullLogger.Instance);

        var stage = new CompressionStage(archiveService);

        await stage.ExecuteAsync(context, CancellationToken.None);

        Assert.True(archiveService.WasCalled);
        Assert.NotNull(context.ArchiveInfo);
        var result = Assert.Single(context.Results, item => item.ObjectType == "Packaging");
        Assert.Equal(ExportObjectStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_WhenCompressionDisabled()
    {
        var archiveService = new StubArchiveService("/tmp/out/Export.zip");
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out") with { EnableCompression = false },
            new RecordingArtifactWriter(),
            NullLogger.Instance);

        var stage = new CompressionStage(archiveService);

        await stage.ExecuteAsync(context, CancellationToken.None);

        Assert.False(archiveService.WasCalled);
        var result = Assert.Single(context.Results, item => item.ObjectType == "Packaging");
        Assert.Equal(ExportObjectStatus.Skipped, result.Status);
    }

    private sealed class StubArchiveService : IExportArchiveService
    {
        private readonly string _archivePath;

        public StubArchiveService(string archivePath)
        {
            _archivePath = archivePath;
        }

        public bool WasCalled { get; private set; }

        public Task<string> CreateArchiveAsync(string outputRoot, string sourceDirectoryName, string archiveFileName, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(_archivePath);
        }
    }

    private sealed class RecordingArtifactWriter : IExportArtifactWriter
    {
        public Task EnsureDirectoryAsync(string relativePath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteArtifactAsync(ExportArtifact artifact, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
