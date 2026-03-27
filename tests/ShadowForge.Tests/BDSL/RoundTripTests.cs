using RPJ = ShadowForge.Formats.RPJ;
using BDSL = ShadowForge.Formats.BDSL;
using ShadowForge.Text;

namespace ShadowForge.Tests.Bdsl;

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
            return; // test data not present - skip silently

        var original = File.ReadAllBytes(path);
        var rpj = RPJ.Reader.Read(original);
        var bdslText = BDSL.Writer.Write(rpj);
        var rpjBack = BDSL.Parser.Parse(bdslText);
        var compiled = RPJ.Writer.Write(rpjBack);

        Assert.Equal(original.Length, compiled.Length);

        int diffs = 0;
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != compiled[i])
            {
                if (IsHeaderStringRegion(i)) continue;
                diffs++;
            }
        }
        Assert.Equal(0, diffs);
    }

    [Fact]
    public void RoundTrip_Synthetic_EmptyScene()
    {
        var rpj = new RPJ.SceneFile();
        System.Text.Encoding.ASCII.GetBytes("0.26").CopyTo(rpj.Header.RawData, 0);
        ShadowForge.IO.BigEndian.WriteUInt32(rpj.Header.RawData, 0x08, 1);
        ShiftJisHelper.WriteFixed(rpj.Header.RawData, 0x0C, 64, "test");
        ShiftJisHelper.WriteFixed(rpj.Header.RawData, 0x4C, 28, "test.rpj");
        rpj.Sections.AreaType = RPJ.AreaType.Town;

        var original = RPJ.Writer.Write(rpj);
        var rpj2 = RPJ.Reader.Read(original);
        var bdslText = BDSL.Writer.Write(rpj2);
        var rpj3 = BDSL.Parser.Parse(bdslText);
        var compiled = RPJ.Writer.Write(rpj3);

        Assert.Equal(original.Length, compiled.Length);
    }

    [Fact]
    public void RoundTrip_Synthetic_WithEntry()
    {
        // Build scene with one entry and one script block
        var rpj = new RPJ.SceneFile();
        System.Text.Encoding.ASCII.GetBytes("0.26").CopyTo(rpj.Header.RawData, 0);
        ShadowForge.IO.BigEndian.WriteUInt32(rpj.Header.RawData, 0x08, 1);
        ShiftJisHelper.WriteFixed(rpj.Header.RawData, 0x0C, 64, "test");
        rpj.Sections.AreaType = RPJ.AreaType.Town;
        rpj.Sections.DataBaseOffset = 0x2B8;

        var entry = new RPJ.Entry();
        ShadowForge.IO.BigEndian.WriteUInt32(entry.RawData, 0x00, 1);
        ShadowForge.IO.BigEndian.WriteUInt32(entry.RawData, 0x24, 0); // spawn
        ShadowForge.IO.BigEndian.WriteFloat(entry.RawData, 0x28, 100f);
        ShadowForge.IO.BigEndian.WriteFloat(entry.RawData, 0x2C, 50f);
        ShadowForge.IO.BigEndian.WriteFloat(entry.RawData, 0x30, -200f);
        ShiftJisHelper.WriteFixed(entry.RawData, 0x04, 20, "NPC");
        rpj.Entries.Add(entry);

        var block = new RPJ.ScriptBlock();
        ShadowForge.IO.BigEndian.WriteUInt32(block.HeaderData, 0x00, 0xFFFFFFFF); // unconditional
        block.Elements.Add(new RPJ.ScriptInstruction { Opcode = 5023, Size = 8, RawParams = [] });
        entry.ScriptBlocks.Add(block);
        rpj.Scripts.Add(block);

        // Round-trip through BDSL
        var bdslText = BDSL.Writer.Write(rpj);
        var rpjBack = BDSL.Parser.Parse(bdslText);

        Assert.Single(rpjBack.Entries);
        Assert.Equal(1u, rpjBack.Entries[0].EntryId);
        Assert.Equal(0u, rpjBack.Entries[0].EntryType);
        Assert.Equal(100f, rpjBack.Entries[0].EntryPosX);
        Assert.Single(rpjBack.Scripts);
        Assert.True(rpjBack.Scripts[0].Header.IsUnconditional);
    }

    private static bool IsHeaderStringRegion(int offset)
    {
        return (offset >= 0x0C && offset <= 0x4B)
            || (offset >= 0x4C && offset <= 0x67)
            || (offset >= 0x68 && offset <= 0x16F)
            || (offset >= 0x170 && offset <= 0x26F);
    }
}
