using TiaProjectExporter.Tia.Inventory.Extraction;

namespace TiaProjectExporter.Tests;

public sealed class FallbackRuntimeClassifierTests
{
    [Theory]
    [InlineData("Siemens.Engineering.HW.DeviceGroup", "Project/Hardware/LineA", "Hardware", "UnmappedHardwareNode")]
    [InlineData("Siemens.Engineering.HmiUnified.ScreenContainer", "Project/HMI/Screens/Main", "HMI", "UnmappedHmiNode")]
    [InlineData("Siemens.Engineering.SW.BlockGroup", "Project/PLC/Blocks/GroupA", "PLC", "UnmappedPlcNode")]
    [InlineData("Siemens.Engineering.Library.MasterCopyFolder", "Project/Libraries/MotorLib", "Libraries", "UnmappedLibraryNode")]
    [InlineData("Siemens.Engineering.Net.ProfinetSubnet", "Project/Network/PN_1", "Network", "UnmappedNetworkNode")]
    public void Classify_ReturnsExpectedDomainAndObjectType(
        string runtimeTypeName,
        string qualifiedPath,
        string expectedDomain,
        string expectedObjectType)
    {
        var classification = FallbackRuntimeClassifier.Classify(runtimeTypeName, qualifiedPath);

        Assert.NotNull(classification);
        Assert.Equal(expectedDomain, classification.Domain);
        Assert.Equal(expectedObjectType, classification.ObjectType);
    }

    [Fact]
    public void Classify_ReturnsNull_ForUnrelatedSimpleType()
    {
        var classification = FallbackRuntimeClassifier.Classify("System.String", "Project/Metadata/Value");

        Assert.Null(classification);
    }
}
