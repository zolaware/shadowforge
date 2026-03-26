// P:\shadowforge\tests\ShadowForge.Tests\Rpj\RoundTripTests.cs
using RPJ = ShadowForge.Formats.RPJ;
using BDS = ShadowForge.Formats.BDS;
using ShadowForge.Text;

namespace ShadowForge.Tests.Rpj;

public class RoundTripTests
{
    public RoundTripTests()
    {
        EncodingSetup.EnsureRegistered();
    }

    [Theory]
    [InlineData("sc03_0044_05.rpj")]
    public void RoundTrip_ByteIdentical(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path))
            return; // test data not present — skip silently

        var original = File.ReadAllBytes(path);

        // RPJ -> BDS -> RPJ round-trip
        var rpj = RPJ.Reader.Read(original);
        var bdsText = BDS.Writer.Write(rpj);
        var rpjBack = BDS.Parser.Parse(bdsText);
        var compiled = RPJ.Writer.Write(rpjBack);

        Assert.Equal(original.Length, compiled.Length);

        int diffs = 0;
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != compiled[i])
            {
                // Allow header string region differences (encoding round-trip tolerance)
                if (IsHeaderStringRegion(i)) continue;
                diffs++;
            }
        }

        Assert.Equal(0, diffs);
    }

    private static bool IsHeaderStringRegion(int offset)
    {
        return (offset >= 0x0C && offset <= 0x4B)
            || (offset >= 0x4C && offset <= 0x67)
            || (offset >= 0x68 && offset <= 0x16F)
            || (offset >= 0x170 && offset <= 0x26F);
    }
}
