using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class RuntimeTypeCatalogStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesRuntimeTypeCatalogArtifacts()
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
                new TiaProjectObjectNode("OpennessRuntime", "TIA Portal V20", "Project/OpennessRuntime", 1, new Dictionary<string, string>
                {
                    ["Version"] = "V20"
                }),
                new TiaProjectObjectNode("FB", "FB_Main", "Project/PLC/Blocks/FB_Main", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "PLC.Blocks",
                    ["RuntimeType"] = "Siemens.Engineering.SW.Blocks.FBBlock",
                    ["TypedExtractor"] = "PlcBlockDomainExtractor",
                    ["FallbackReflectionUsed"] = "false"
                }),
                new TiaProjectObjectNode("UnmappedHmiNode", "ScreenContainer", "Project/HMI/Screens/Container", 2, new Dictionary<string, string>
                {
                    ["Domain"] = "HMI",
                    ["RuntimeType"] = "Siemens.Engineering.Hmi.ScreenContainer",
                    ["FallbackReflectionUsed"] = "true"
                })
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new RuntimeTypeCatalogStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var markdown = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/RUNTIME_TYPE_CATALOG.md");
        var json = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/RUNTIME_TYPE_CATALOG.json");

        Assert.Contains("Detected TIA version context", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("Top Suggestions", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("ScreenContainer", markdown.Content, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json.Content);
        Assert.Equal("V20", document.RootElement.GetProperty("tiaVersion").GetString());

        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Contains(entries, entry => entry.GetProperty("runtimeType").GetString() == "Siemens.Engineering.Hmi.ScreenContainer");
        Assert.Contains(entries, entry => entry.GetProperty("typedExtractor").GetString() == "PlcBlockDomainExtractor");
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
