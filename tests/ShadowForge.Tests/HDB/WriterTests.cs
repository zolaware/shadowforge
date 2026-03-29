using ShadowForge.Formats.HDB;

namespace ShadowForge.Tests.Hdb;

public class WriterTests
{
    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void RoundTrip_ByteIdentical(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var original = File.ReadAllBytes(path);
        var model = Reader.Read(original);
        var compiled = Writer.Write(model);

        Assert.Equal(original.Length, compiled.Length);
        for (int i = 0; i < original.Length; i++)
            Assert.True(original[i] == compiled[i],
                $"Mismatch at byte 0x{i:X}: expected 0x{original[i]:X2}, got 0x{compiled[i]:X2}");
    }
}
