using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Repository;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class RepositoryLayoutStageTests
{
    [Fact]
    public async Task ExecuteAsync_CreatesExpectedDirectoriesAndCoreArtifacts()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var stage = new RepositoryLayoutStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ExportRepositoryLayout.Directories.Count, writer.Directories.Count);

        foreach (var directory in ExportRepositoryLayout.Directories)
        {
            Assert.Contains(directory, writer.Directories);
        }

        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath == "Export/README.md");
        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath == "Export/PROJECT_STATISTICS.json");
    }

    [Fact]
    public async Task ExecuteAsync_SkipsMarkdownArtifacts_WhenMarkdownSummariesDisabled()
    {
        var writer = new RecordingArtifactWriter();
        var options = new ExportOptions(
            ProjectPath: null,
            OutputDirectory: "out",
            Formats: new[] { ExportFormat.Json, ExportFormat.Xml },
            EnableCompression: false,
            SkipDiagnostics: false,
            GenerateMarkdownSummaries: false);

        var context = new ExportExecutionContext(options, writer, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var stage = new RepositoryLayoutStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        Assert.DoesNotContain(writer.Artifacts, artifact => artifact.RelativePath == "Export/README.md");
        Assert.DoesNotContain(writer.Artifacts, artifact => artifact.RelativePath == "Export/PROJECT_OVERVIEW.md");
        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath == "Export/PROJECT_STATISTICS.json");
    }

    private sealed class RecordingArtifactWriter : IExportArtifactWriter
    {
        public List<string> Directories { get; } = [];

        public List<ExportArtifact> Artifacts { get; } = [];

        public Task EnsureDirectoryAsync(string relativePath, CancellationToken cancellationToken)
        {
            Directories.Add(relativePath);
            return Task.CompletedTask;
        }

        public Task WriteArtifactAsync(ExportArtifact artifact, CancellationToken cancellationToken)
        {
            Artifacts.Add(artifact);
            return Task.CompletedTask;
        }
    }
}
