// P:\shadowforge\tests\ShadowForge.Tests\Rpj\ReaderTests.cs
using ShadowForge.Formats.RPJ;
using ShadowForge.Text;

namespace ShadowForge.Tests.Rpj;

public class ReaderTests
{
    public ReaderTests()
    {
        EncodingSetup.EnsureRegistered();
    }

    [Fact]
    public void Read_MinimalFile_ParsesHeader()
    {
        // Minimal valid RPJ: 0x2B8 header+section table, 1 entry at DataBase, no scripts, no waypoints
        var data = new byte[0x2B8 + 0x70]; // header + section table + 1 entry

        // Write version "0.26" at offset 0
        data[0] = (byte)'0'; data[1] = (byte)'.'; data[2] = (byte)'2'; data[3] = (byte)'6';

        // Section table at 0x270: AreaType = 3 (dungeon) at +0x04
        WriteBE(data, 0x274, 3);
        // DataBase = 0x2B8 at +0x3C relative to 0x270
        WriteBE(data, 0x2AC, 0x2B8);

        // Entry at 0x2B8: ID=1 at +0x00, next=0 at +0x6C (end of list)
        WriteBE(data, 0x2B8, 1);

        var rpj = Reader.Read(data);

        Assert.Equal("0.26", rpj.Header.Version);
        Assert.Equal(AreaType.Dungeon, rpj.Sections.AreaType);
        Assert.Single(rpj.Entries);
        Assert.Equal(1u, rpj.Entries[0].EntryId);
    }

    [Fact]
    public void Read_TwoEntryChain_FollowsLinkedList()
    {
        var data = new byte[0x2B8 + 0x70 * 2];

        WriteBE(data, 0x2AC, 0x2B8); // DataBase
        WriteBE(data, 0x2A4, 0xE0);  // Section1 = 2 * 0x70

        // Entry 0 at 0x2B8: ID=1, next=0x70
        WriteBE(data, 0x2B8 + 0x00, 1);
        WriteBE(data, 0x2B8 + 0x6C, 0x70);

        // Entry 1 at 0x328: ID=2, next=0 (end)
        WriteBE(data, 0x328 + 0x00, 2);
        WriteBE(data, 0x328 + 0x6C, 0);

        var rpj = Reader.Read(data);

        Assert.Equal(2, rpj.Entries.Count);
        Assert.Equal(1u, rpj.Entries[0].EntryId);
        Assert.Equal(2u, rpj.Entries[1].EntryId);
    }

    [Fact]
    public void Read_EmptyEntryPool_ReturnsNoEntries()
    {
        var data = new byte[0x2B8];
        WriteBE(data, 0x2AC, 0); // DataBase = 0 (no entries)

        var rpj = Reader.Read(data);

        Assert.Empty(rpj.Entries);
    }

    private static void WriteBE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)((value >> 24) & 0xFF);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }
}
