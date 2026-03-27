using ShadowForge.Formats.HDB;

namespace ShadowForge.Tests.Hdb;

public class ReaderTests
{
    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void Read_ValidFile_ParsesHeader(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var model = Reader.Read(path);

        Assert.Equal(FileHeader.Magic, model.Header.MagicValue);
        Assert.NotEmpty(model.Bones);
        Assert.NotEmpty(model.Textures);
        Assert.NotEmpty(model.VertexArrays);
    }

    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void Read_ValidFile_ParsesBones(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var model = Reader.Read(path);

        var firstBone = model.Bones[0];
        Assert.False(string.IsNullOrEmpty(firstBone.Name));
        Assert.Equal(0, firstBone.Index);
    }

    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void Read_ValidFile_ParsesTextures(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var model = Reader.Read(path);

        Assert.Equal(model.TextureCount, model.Textures.Count);
        Assert.All(model.Textures, t => Assert.False(string.IsNullOrEmpty(t.Name)));
    }
}
