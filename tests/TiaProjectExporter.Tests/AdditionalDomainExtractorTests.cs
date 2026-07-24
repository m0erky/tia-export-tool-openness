using TiaProjectExporter.Tia.Inventory.Extraction;

namespace TiaProjectExporter.Tests;

public sealed class AdditionalDomainExtractorTests
{
    [Fact]
    public void LibraryDomainExtractor_ExtractsLibraryTypeNode()
    {
        var extractor = new LibraryDomainExtractor();
        var runtime = new GlobalLibraryTypeRuntime
        {
            Name = "MotorLib",
            TypeVersions = new[] { new NamedNode("v1.0"), new NamedNode("v1.1") }
        };

        var node = extractor.TryExtract(runtime, "Project/Libraries/MotorLib", 2);

        Assert.NotNull(node);
        Assert.Equal("Libraries", node.Metadata?["Domain"]);
        Assert.Contains("v1.0", node.Metadata?["Versions"]);
    }

    [Fact]
    public void DiagnosticsDomainExtractor_ExtractsAlarmNode()
    {
        var extractor = new DiagnosticsDomainExtractor();
        var runtime = new AlarmDiagnosticRuntime
        {
            Name = "Alarm_Overheat",
            Severity = "High"
        };

        var node = extractor.TryExtract(runtime, "Project/Diagnostics/Alarm_Overheat", 2);

        Assert.NotNull(node);
        Assert.Equal("Alarm", node.ObjectType);
        Assert.Equal("Diagnostics", node.Metadata?["Domain"]);
        Assert.Equal("High", node.Metadata?["Severity"]);
    }

    [Fact]
    public void UsersAuditDomainExtractor_ExtractsUserNode()
    {
        var extractor = new UsersAuditDomainExtractor();
        var runtime = new AuditUserRuntime
        {
            Name = "OperatorA",
            Roles = new[] { new NamedNode("Operator") }
        };

        var node = extractor.TryExtract(runtime, "Project/Users/OperatorA", 2);

        Assert.NotNull(node);
        Assert.Equal("UsersAudit", node.Metadata?["Domain"]);
        Assert.Contains("Operator", node.Metadata?["Permissions"]);
    }

    [Fact]
    public void TechnologyDomainExtractor_ExtractsSafetyNode()
    {
        var extractor = new TechnologyDomainExtractor();
        var runtime = new SafetyAxisRuntime
        {
            Name = "SafetyAxis_1",
            Axes = new[] { new NamedNode("Axis_X") }
        };

        var node = extractor.TryExtract(runtime, "Project/Technology/SafetyAxis_1", 2);

        Assert.NotNull(node);
        Assert.Equal("Safety", node.ObjectType);
        Assert.Equal("Technology", node.Metadata?["Domain"]);
        Assert.Contains("Axis_X", node.Metadata?["Dependencies"]);
    }

    [Fact]
    public void HmiScreenFaceplateDomainExtractor_ExtractsFaceplate()
    {
        var extractor = new HmiScreenFaceplateDomainExtractor();
        var runtime = new WinccFaceplateRuntime
        {
            Name = "MotorFaceplate"
        };

        var node = extractor.TryExtract(runtime, "Project/HMI/Faceplates/MotorFaceplate", 2);

        Assert.NotNull(node);
        Assert.Equal("Faceplate", node.ObjectType);
        Assert.Equal("HMI", node.Metadata?["Domain"]);
        Assert.Equal("Faceplate", node.Metadata?["HmiSubdomain"]);
    }

    [Fact]
    public void HmiRecipeAlarmScriptDomainExtractor_ExtractsRecipe()
    {
        var extractor = new HmiRecipeAlarmScriptDomainExtractor();
        var runtime = new RecipeRuntime
        {
            Name = "DefaultRecipe",
            Connections = new[] { new NamedNode("PLC_1") }
        };

        var node = extractor.TryExtract(runtime, "Project/HMI/Recipes/DefaultRecipe", 2);

        Assert.NotNull(node);
        Assert.Equal("Recipe", node.ObjectType);
        Assert.Equal("HMI", node.Metadata?["Domain"]);
        Assert.Contains("PLC_1", node.Metadata?["Dependencies"]);
    }

    [Fact]
    public void ProjectHierarchyDomainExtractor_ExtractsDeviceGroup()
    {
        var extractor = new ProjectHierarchyDomainExtractor();
        var runtime = new DeviceGroupRuntime
        {
            Name = "LineA"
        };

        var node = extractor.TryExtract(runtime, "Project/Groups/LineA", 2);

        Assert.NotNull(node);
        Assert.Equal("DeviceGroup", node.ObjectType);
        Assert.Equal("Project", node.Metadata?["Domain"]);
        Assert.Equal("true", node.Metadata?["Hierarchy"]);
    }

    private sealed class NamedNode
    {
        public NamedNode(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class GlobalLibraryTypeRuntime
    {
        public string? Name { get; init; }

        public IEnumerable<NamedNode>? TypeVersions { get; init; }
    }

    private sealed class AlarmDiagnosticRuntime
    {
        public string? Name { get; init; }

        public string? Severity { get; init; }
    }

    private sealed class AuditUserRuntime
    {
        public string? Name { get; init; }

        public IEnumerable<NamedNode>? Roles { get; init; }
    }

    private sealed class SafetyAxisRuntime
    {
        public string? Name { get; init; }

        public IEnumerable<NamedNode>? Axes { get; init; }
    }

    private sealed class WinccFaceplateRuntime
    {
        public string? Name { get; init; }
    }

    private sealed class RecipeRuntime
    {
        public string? Name { get; init; }

        public IEnumerable<NamedNode>? Connections { get; init; }
    }

    private sealed class DeviceGroupRuntime
    {
        public string? Name { get; init; }
    }
}
