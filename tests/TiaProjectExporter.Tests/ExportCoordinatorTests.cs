using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Application.Services;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tests;

public sealed class ExportCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_ContinuesWhenAStageFails()
    {
        var stages = new IExportStage[]
        {
            new StubStage("Stage 1", static context =>
            {
                context.AddResult(new ExportedObjectResult("Test", "First", ExportObjectStatus.Succeeded));
                return Task.CompletedTask;
            }),
            new StubStage("Stage 2", static _ => Task.FromException(new InvalidOperationException("Boom"))),
            new StubStage("Stage 3", static context =>
            {
                context.AddResult(new ExportedObjectResult("Test", "Third", ExportObjectStatus.Succeeded));
                return Task.CompletedTask;
            })
        };

        var coordinator = new ExportCoordinator(
            stages,
            new StubArtifactWriterFactory(),
            NullLogger<ExportCoordinator>.Instance);

        var report = await coordinator.ExecuteAsync(
            ExportOptions.CreateDefault("out"),
            progressCallback: null,
            preloadedInventory: null,
            CancellationToken.None);

        Assert.Equal(5, report.Results.Count);
        Assert.Equal(4, report.SucceededCount);
        Assert.Equal(1, report.FailedCount);
        Assert.Single(report.Issues);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgressFromStages()
    {
        var progressUpdates = new List<ExportProgressUpdate>();

        var coordinator = new ExportCoordinator(
            new[]
            {
                new StubStage(
                    "Stage 1",
                    static context => context.ReportProgressAsync(new ExportProgressUpdate("Stage 1", "Object A", 1, 2, TimeSpan.FromSeconds(3))))
            },
            new StubArtifactWriterFactory(),
            NullLogger<ExportCoordinator>.Instance);

        await coordinator.ExecuteAsync(
            ExportOptions.CreateDefault("out"),
            update =>
            {
                progressUpdates.Add(update);
                return Task.CompletedTask;
            },
            preloadedInventory: null,
            CancellationToken.None);

        Assert.Contains(progressUpdates, update => update.CurrentObject == "Object A");
    }

    private sealed class StubStage : IExportStage
    {
        private readonly Func<ExportExecutionContext, Task> _executeAsync;

        public StubStage(string name, Func<ExportExecutionContext, Task> executeAsync)
        {
            Name = name;
            _executeAsync = executeAsync;
        }

        public string Name { get; }

        public Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken) =>
            _executeAsync(context);
    }

    private sealed class StubArtifactWriterFactory : IExportArtifactWriterFactory
    {
        public IExportArtifactWriter Create(string outputRoot) => new StubArtifactWriter();
    }

    private sealed class StubArtifactWriter : IExportArtifactWriter
    {
        public Task EnsureDirectoryAsync(string relativePath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteArtifactAsync(ExportArtifact artifact, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
