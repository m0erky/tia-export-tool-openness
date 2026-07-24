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
}
