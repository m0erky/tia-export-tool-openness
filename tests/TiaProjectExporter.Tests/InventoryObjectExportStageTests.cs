using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;
using System.Text.Json;

namespace TiaProjectExporter.Tests;

public sealed class InventoryObjectExportStageTests
{
    [Fact]
    public async Task ExecuteAsync_WritesDomainTypeBundlesWithDeepContent()
    {
        var writer = new RecordingArtifactWriter();
        var context = new ExportExecutionContext(
            ExportOptions.CreateDefault("out"),
            writer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        context.SetInventory(new TiaProjectInventory(
            TiaInventoryStatus.Complete,
            ProjectName: "Sample",
            ProjectPath: "sample.ap20",
            Objects:
            [
                new TiaProjectObjectNode(
                    ObjectType: "Device",
                    Name: "PLC_1",
                    QualifiedPath: "Project/Devices/PLC_1",
                    Depth: 1,
                    Metadata: new Dictionary<string, string> { ["RuntimeType"] = "DeviceType" }),
                new TiaProjectObjectNode(
                    ObjectType: "FB",
                    Name: "FB100",
                    QualifiedPath: "Project/Devices/PLC_1/Software/Blocks/FB100",
                    Depth: 2,
                    Metadata: new Dictionary<string, string>
                    {
                        ["CanonicalQualifiedPath"] = "Project/Devices/PLC_1/Software/Blocks/FB100",
                        ["OriginalQualifiedPaths"] = "Project/Devices/PLC_1/Software/Blocks/FB100",
                        ["BlockNumber"] = "100",
                        ["Content.ExportXml"] = """
                                                <Document>
                                                  <StructuredText>
                                                    <Token Text="IF" />
                                                    <Blank />
                                                    <Access>
                                                      <Symbol>
                                                        <Component Name="Axis" />
                                                        <Component Name="Ready" />
                                                      </Symbol>
                                                    </Access>
                                                    <Blank />
                                                    <Token Text="THEN" />
                                                    <NewLine />
                                                    <Token Text="END_IF;" />
                                                  </StructuredText>
                                                </Document>
                                                """,
                        ["Content.SourceText"] = "   //"
                    }),
                new TiaProjectObjectNode(
                    ObjectType: "OB",
                    Name: "Main/Startup",
                    QualifiedPath: "Project/Devices/PLC_1/Software/Blocks/Main/Startup",
                    Depth: 2,
                    Metadata: new Dictionary<string, string>
                    {
                        ["CanonicalQualifiedPath"] = "Project/Devices/PLC_1/Software/Blocks/Main/Startup",
                        ["OriginalQualifiedPaths"] = "Project/Devices/PLC_1/Software/Blocks/Main/Startup",
                        ["BlockNumber"] = "1",
                        ["Content.SourceText"] = "ORGANIZATION_BLOCK Main"
                    })
            ],
            Issues: Array.Empty<ExportIssue>()));

        var stage = new InventoryObjectExportStage();

        await stage.ExecuteAsync(context, CancellationToken.None);

        var blocksJson = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Blocks/Bundles/FB.json");
        Assert.Contains("sourceTextContent", blocksJson.Content, StringComparison.Ordinal);

        var blocksMarkdown = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Blocks/Bundles/FB.md");
        Assert.Contains("```text", blocksMarkdown.Content, StringComparison.Ordinal);
        Assert.Contains("```xml", blocksMarkdown.Content, StringComparison.Ordinal);

        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath == "Export/Hardware/Bundles/Device.json");

        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath == "Export/Blocks/ByName/FB_FB100.json");
        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath == "Export/Blocks/ByName/FB_FB100.md");
        Assert.Contains(writer.Artifacts, artifact => artifact.RelativePath == "Export/Blocks/ByName/OB_Main_Startup.json");

        var fbByNameJson = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Blocks/ByName/FB_FB100.json");
        using var byNameDocument = JsonDocument.Parse(fbByNameJson.Content);
        var byNameRoot = byNameDocument.RootElement;
        Assert.Equal("Success", byNameRoot.GetProperty("reconstructionStatus").GetString());
        var reconstructedSource = byNameRoot.GetProperty("reconstructedSourceText").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reconstructedSource));
        Assert.Contains("IF Axis.Ready THEN", reconstructedSource!, StringComparison.Ordinal);

        var index = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Blocks/ByName/INDEX.json");
        Assert.Contains("FB_FB100.json", index.Content, StringComparison.Ordinal);
        Assert.Contains("OB_Main_Startup.json", index.Content, StringComparison.Ordinal);

        var result = Assert.Single(context.Results, item => item.ObjectType == "InventoryObjects");
        Assert.Equal(ExportObjectStatus.Succeeded, result.Status);
        Assert.Contains("bundles", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("per-block", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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
