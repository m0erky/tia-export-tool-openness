using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class MappingImplementationTrackerStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesTrackerArtifactsAndTrend()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "tia-export-tracker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);

        try
        {
            var stage = new MappingImplementationTrackerStage();

            var writer1 = new RecordingArtifactWriter();
            var context1 = new ExportExecutionContext(
                ExportOptions.CreateDefault(outputDir),
                writer1,
                NullLogger.Instance);

            context1.SetInventory(new TiaProjectInventory(
                TiaInventoryStatus.Partial,
                "Demo",
                "C:/Projects/Demo.ap19",
                new[]
                {
                    new TiaProjectObjectNode("UnmappedHmiNode", "ScreenContainer", "Project/HMI/Screens/Container", 2, new Dictionary<string, string>
                    {
                        ["Domain"] = "HMI",
                        ["RuntimeType"] = "Siemens.Engineering.Hmi.ScreenContainer",
                        ["FallbackReflectionUsed"] = "true",
                        ["ExtractedByTypedExtractor"] = "false",
                        ["References"] = "MissingTag"
                    })
                },
                Array.Empty<ExportIssue>()));

            await stage.ExecuteAsync(context1, CancellationToken.None);

            var firstJson = Assert.Single(writer1.Artifacts, artifact => artifact.RelativePath == "Export/Reports/MAPPING_IMPLEMENTATION_TRACKER.json");
            using (var firstDoc = JsonDocument.Parse(firstJson.Content))
            {
                Assert.False(firstDoc.RootElement.GetProperty("trend").GetProperty("hasPreviousSnapshot").GetBoolean());
            }

            var writer2 = new RecordingArtifactWriter();
            var context2 = new ExportExecutionContext(
                ExportOptions.CreateDefault(outputDir),
                writer2,
                NullLogger.Instance);

            context2.SetInventory(new TiaProjectInventory(
                TiaInventoryStatus.Partial,
                "Demo",
                "C:/Projects/Demo.ap19",
                new[]
                {
                    new TiaProjectObjectNode("Screen", "MainScreen", "Project/HMI/Screens/MainScreen", 2, new Dictionary<string, string>
                    {
                        ["Domain"] = "HMI",
                        ["RuntimeType"] = "Siemens.Engineering.Hmi.ScreenContainer",
                        ["TypedExtractor"] = "HmiScreenFaceplateDomainExtractor",
                        ["FallbackReflectionUsed"] = "false",
                        ["ExtractedByTypedExtractor"] = "true"
                    })
                },
                Array.Empty<ExportIssue>()));

            await stage.ExecuteAsync(context2, CancellationToken.None);

            var secondMarkdown = Assert.Single(writer2.Artifacts, artifact => artifact.RelativePath == "Export/Reports/MAPPING_IMPLEMENTATION_TRACKER.md");
            var secondJson = Assert.Single(writer2.Artifacts, artifact => artifact.RelativePath == "Export/Reports/MAPPING_IMPLEMENTATION_TRACKER.json");

            Assert.Contains("Trend", secondMarkdown.Content, StringComparison.Ordinal);
            Assert.Contains("Mapping completion rate", secondMarkdown.Content, StringComparison.Ordinal);

            using var secondDoc = JsonDocument.Parse(secondJson.Content);
            Assert.True(secondDoc.RootElement.GetProperty("trend").GetProperty("hasPreviousSnapshot").GetBoolean());
            Assert.True(secondDoc.RootElement.GetProperty("snapshot").GetProperty("completionRate").GetDouble() >= 0);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    private sealed class RecordingArtifactWriter : IExportArtifactWriter
    {
        public List<ExportArtifact> Artifacts { get; } = [];

        public Task EnsureDirectoryAsync(string relativePath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteArtifactAsync(ExportArtifact artifact, CancellationToken cancellationToken)
        {
            Artifacts.Add(artifact);
            return Task.CompletedTask;
        }
    }
}
