using ShadowForge.Formats.HDB;

namespace ShadowForge.Tests.Hdb;

public class VertexFormatTests
{
    [Fact]
    public void MultiInfluence_Type_Has_Stride_100()
    {
        int stride = VertexFormat.ComputeStride(0x13F00000);
        Assert.Equal(100, stride);
    }

    [Fact]
    public void MultiInfluence_Type_Has_3_BoneInfluences()
    {
        int count = VertexFormat.CountBoneInfluences(0x13F00000);
        Assert.Equal(3, count);
    }

    [Fact]
    public void SingleInfluence_48Byte_Stride()
    {
        // A typical single-influence format: position + normal + UV + bone
        // Exact value varies per model; test with a known value from disc
        // For now, test that a format with no influence bits gives stride from low 12 bits
        uint vaType = 0x00000030; // 48 in low 12 bits, no high flags
        int stride = VertexFormat.ComputeStride(vaType);
        Assert.Equal(48, stride);
    }

    [Fact]
    public void Components_MultiInfluence_Has_Expected_Semantics()
    {
        var components = VertexFormat.GetComponents(0x13F00000);
        Assert.Contains(components, c => c.Semantic == VertexSemantic.Position);
        Assert.Contains(components, c => c.Semantic == VertexSemantic.TexCoord4);
        Assert.Contains(components, c => c.Semantic == VertexSemantic.Influence2PosWeight);
    }

    [Fact]
    public void Components_DirectStride_Returns_Empty()
    {
        // When low 12 bits encode stride directly, no component flags are set
        var components = VertexFormat.GetComponents(0x00000030);
        Assert.Empty(components);
    }
}
