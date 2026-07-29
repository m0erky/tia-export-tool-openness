using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class ReportDomainConsistencyTests
{
    [Fact]
    public async Task CoverageReadinessAndNextBestActions_UseSameDiscoveredCountsPerDomain()
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
                new TiaProjectObjectNode("OB", "Main", "Project/PLC/Blocks/Main", 2, new Dictionary<string, string>
                {
                    ["ExtractionConfidence"] = "0.92",
                    ["ExtractedByTypedExtractor"] = "true"
                }),
                new TiaProjectObjectNode("InstanceDB", "Block_1_DB", "Project/PLC/Blocks/Block_1_DB", 2, new Dictionary<string, string>
                {
                    ["ExtractionConfidence"] = "0.90"
                }),
                new TiaProjectObjectNode("UnmappedRuntimeNode", "UDT_Motor", "Project/Devices/CPU_1/TypeGroup/Types/UDT_Motor", 3, new Dictionary<string, string>
                {
                    ["RuntimeType"] = "Siemens.Engineering.SW.Types.PlcStructType"
                }),
                new TiaProjectObjectNode("DeviceItem", "DeviceItem_1", "Project/Devices/CPU_1/DeviceItem_1", 2, new Dictionary<string, string>
                {
                    ["ExtractionConfidence"] = "0.80",
                    ["ExtractedByTypedExtractor"] = "true"
                })
            ],
            Issues:
            [
                new ExportIssue("Blocks", "Sample issue for blocks")
            ]));

        await new ExportCoverageMatrixStage().ExecuteAsync(context, CancellationToken.None);
        await new ExportReadinessStage().ExecuteAsync(context, CancellationToken.None);
        await new NextBestActionsStage().ExecuteAsync(context, CancellationToken.None);

        var coverageJson = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/EXPORT_COVERAGE_MATRIX.json");
        var readinessJson = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/EXPORT_READINESS_SCORE.json");
        var nbaJson = Assert.Single(writer.Artifacts, artifact => artifact.RelativePath == "Export/Reports/NEXT_BEST_ACTIONS.json");

        using var coverageDoc = JsonDocument.Parse(coverageJson.Content);
        using var readinessDoc = JsonDocument.Parse(readinessJson.Content);
        using var nbaDoc = JsonDocument.Parse(nbaJson.Content);

        var coverageBlocks = GetDiscoveredForDomain(coverageDoc.RootElement.GetProperty("domains"), "PLC.Blocks");
        var readinessBlocks = GetDiscoveredForDomain(readinessDoc.RootElement.GetProperty("domains"), "PLC.Blocks");
        var nbaBlocks = GetDiscoveredForDomain(nbaDoc.RootElement.GetProperty("domainInputs"), "PLC.Blocks");

        Assert.Equal(coverageBlocks, readinessBlocks);
        Assert.Equal(coverageBlocks, nbaBlocks);

        var coverageDataTypes = GetDiscoveredForDomain(coverageDoc.RootElement.GetProperty("domains"), "PLC.DataTypes");
        Assert.True(coverageDataTypes > 0);
    }

    private static int GetDiscoveredForDomain(JsonElement domains, string domain)
    {
        foreach (var entry in domains.EnumerateArray())
        {
            if (entry.GetProperty("domain").GetString() == domain)
            {
                return entry.GetProperty("discovered").GetInt32();
            }
        }

        throw new InvalidOperationException($"Domain '{domain}' not found in payload.");
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
