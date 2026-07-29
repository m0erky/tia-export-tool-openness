using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Tests;

public sealed class StructuredTextReconstructorTests
{
    [Fact]
    public void Reconstruct_ParsesIfThenEndIf_WithAccessAndConstants()
    {
        var xml = """
                  <Root>
                    <StructuredText>
                      <Token Text="IF" />
                      <Blank />
                      <Access>
                        <Symbol>
                          <Component Name="MyDb" />
                          <Component Name="Value" />
                        </Symbol>
                      </Access>
                      <Blank />
                      <Token Text="&gt;" />
                      <Blank />
                      <ConstantValue>10</ConstantValue>
                      <Blank />
                      <Token Text="THEN" />
                      <NewLine />
                      <LineComment>
                        <Text>check threshold</Text>
                      </LineComment>
                      <NewLine />
                      <Token Text="END_IF;" />
                    </StructuredText>
                  </Root>
                  """;

        var result = StructuredTextReconstructor.Reconstruct(xml);

        Assert.Equal("Success", result.ReconstructionStatus);
        Assert.NotNull(result.ReconstructedSourceText);
        Assert.Contains("IF MyDb.Value > 10 THEN", result.ReconstructedSourceText!, StringComparison.Ordinal);
        Assert.Contains("// check threshold", result.ReconstructedSourceText!, StringComparison.Ordinal);
        Assert.Contains("END_IF;", result.ReconstructedSourceText!, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconstruct_DecodesXmlEntities()
    {
        var xml = """
                  <Root>
                    <StructuredText>
                      <Token Text="A" />
                      <Blank />
                      <Token Text="&lt;=" />
                      <Blank />
                      <ConstantValue>&amp;B</ConstantValue>
                    </StructuredText>
                  </Root>
                  """;

        var result = StructuredTextReconstructor.Reconstruct(xml);

        Assert.Equal("Success", result.ReconstructionStatus);
        Assert.Equal("A <= &B", result.ReconstructedSourceText);
    }

    [Fact]
    public void Reconstruct_ReturnsNoStructuredText_WhenNodeMissing()
    {
        var xml = """
                  <Root>
                    <Source>FUNCTION_BLOCK FB100</Source>
                  </Root>
                  """;

        var result = StructuredTextReconstructor.Reconstruct(xml);

        Assert.Equal("NoStructuredText", result.ReconstructionStatus);
        Assert.Null(result.ReconstructedSourceText);
    }

    [Fact]
    public void Reconstruct_ReturnsParseError_OnInvalidXml()
    {
        var result = StructuredTextReconstructor.Reconstruct("<Root><StructuredText>");

        Assert.Equal("ParseError", result.ReconstructionStatus);
        Assert.Null(result.ReconstructedSourceText);
    }

    [Fact]
    public void Reconstruct_AwlInstructions_AreReconstructedInOrder()
    {
        var xml = """
                  <Root>
                    <StructuredText>
                      <Token Text="L" />
                      <Blank />
                      <ConstantValue>5</ConstantValue>
                      <NewLine />
                      <Token Text="T" />
                      <Blank />
                      <Access>
                        <Symbol>
                          <Component Name="DB1" />
                          <Component Name="TargetValue" />
                        </Symbol>
                      </Access>
                    </StructuredText>
                  </Root>
                  """;

        var result = StructuredTextReconstructor.Reconstruct(xml, "AWL");

        Assert.Equal("Success", result.ReconstructionStatus);
        Assert.Equal("L 5\nT DB1.TargetValue", result.ReconstructedSourceText);
    }

    [Fact]
    public void Reconstruct_AwlAccessSymbolPath_IsResolvedAsDotPath()
    {
        var xml = """
                  <Root>
                    <StructuredText>
                      <Token Text="U" />
                      <Blank />
                      <Access>
                        <Symbol>
                          <Component Name="MyDb" />
                          <Component Name="Flags" />
                          <Component Name="Ready" />
                        </Symbol>
                      </Access>
                    </StructuredText>
                  </Root>
                  """;

        var result = StructuredTextReconstructor.Reconstruct(xml, "AWL");

        Assert.Equal("Success", result.ReconstructionStatus);
        Assert.Equal("U MyDb.Flags.Ready", result.ReconstructedSourceText);
    }

    [Fact]
    public void Reconstruct_AwlDecodesEntities()
    {
        var xml = """
                  <Root>
                    <StructuredText>
                      <Token Text="L" />
                      <Blank />
                      <ConstantValue>&amp;MyConst</ConstantValue>
                    </StructuredText>
                  </Root>
                  """;

        var result = StructuredTextReconstructor.Reconstruct(xml, "AWL");

        Assert.Equal("Success", result.ReconstructionStatus);
        Assert.Equal("L &MyConst", result.ReconstructedSourceText);
    }
}
