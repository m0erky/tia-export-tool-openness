using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class MultilingualTextStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesMultilingualTextArtifacts()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            NullLogger.Instance);

        context.SetInventory(new TiaProjectInventory(
            TiaInventoryStatus.Partial,
            ProjectName: "Demo",
            ProjectPath: "C:/Projects/Demo.ap19",
            Objects:
            [
                new TiaProjectObjectNode("FB", "FB_Main", "Project/PLC/Blocks/FB_Main", 2, new Dictionary<string, string>
                {
                    ["Comment_en-US"] = "Main production routine",
                    ["Comment_de-DE"] = "Hauptproduktionsablauf"
                }),
                new TiaProjectObjectNode("Tag", "Tag_Start", "Project/Tags/Default/Tag_Start", 2, new Dictionary<string, string>
                {
                    ["Text_en-US"] = "Start command"
                })
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new MultilingualTextStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var jsonArtifact = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Metadata/MULTILINGUAL_TEXTS.json");
        var mdArtifact = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Metadata/MULTILINGUAL_TEXTS.md");

        using var document = JsonDocument.Parse(jsonArtifact.Content);
        Assert.Equal(3, document.RootElement.GetProperty("entryCount").GetInt32());
        Assert.Contains("Hauptproduktionsablauf", mdArtifact.Content, StringComparison.Ordinal);
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
