using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tests;

public sealed class QualifiedPathCanonicalizerTests
{
    [Theory]
    [InlineData("Project/DeviceItemImpl/DeviceItemImpl/Services/Software", "Project/DeviceItemImpl/Services/Software")]
    [InlineData("Project/BlockGroup/Blocks/Main", "Project/BlockGroup/Main")]
    [InlineData("Project/DeviceItemImpl/BlockGroup/Blocks/FB_1", "Project/DeviceItemImpl/BlockGroup/FB_1")]
    [InlineData("Project/DeviceItemImpl/Services/Software", "Project/DeviceItemImpl/Services/Software")]
    public void Canonicalize_NormalizesKnownVariantPatterns(string input, string expected)
    {
        var result = QualifiedPathCanonicalizer.Canonicalize(input);

        Assert.Equal(expected, result);
    }
}

