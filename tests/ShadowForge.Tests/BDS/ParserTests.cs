// P:\shadowforge\tests\ShadowForge.Tests\Bds\ParserTests.cs
using ShadowForge.Formats.BDS;
using ShadowForge.Formats.RPJ;
using ShadowForge.Text;

namespace ShadowForge.Tests.Bds;

public class ParserTests
{
    public ParserTests()
    {
        EncodingSetup.EnsureRegistered();
    }

    [Fact]
    public void Parse_HeaderDirectives()
    {
        var bds = """
            @version "0.26"
            @flags 1
            @scene "test"
            @filename "test.rpj"
            @area dungeon
            @total_cmds 100
            @entry_count 5
            @file_size 4096
            @section1 0x2A0
            @section2 0xC10
            @section3 0x40
            @section4 0x1168
            @data_base 0x2B8
            @reserved0 0x00000000
            @reserved1 0x00000000 0x00000000 0x00000000 0x00000000
            @reserved2 0x00000000 0x00000000 0x00000000 0x00000000
            @reserved3 0x00000000
            """;

        var rpj = Parser.Parse(bds);

        Assert.Equal("0.26", rpj.Header.Version);
        Assert.Equal(AreaType.Dungeon, rpj.Sections.AreaType);
        Assert.Equal(100u, rpj.Sections.TotalScriptCmds);
        Assert.Equal(0x2A0u, rpj.Sections.Section1Offset);
        Assert.Equal(0x2B8u, rpj.Sections.DataBaseOffset);
    }

    [Fact]
    public void Parse_EntryBlock()
    {
        var bds = """
            @version "0.26"
            @data_base 0x2B8

            entry "test" id=1 type=event offset=0x2B8 {
                name_raw = 7465737400000000000000000000000000000000
                reserved = 0000000000000000
                ref_id = 0
                position = (1.5, 2.0, 3.0)
                radius = 180
                target_refs = 0x00000000 0x00000000 0x00000000
                padding = 0000000000000000000000000000000000000000
                overlap = 000000000000000000000000
                script_block_count = 0
                script_block_offset = 0x0
                next_entry = 0x0
            }
            """;

        var rpj = Parser.Parse(bds);

        Assert.Single(rpj.Entries);
        var entry = rpj.Entries[0];
        Assert.Equal(1u, entry.EntryId);
        Assert.Equal(6u, entry.EntryType); // event = 6
        Assert.Equal(0x2B8, entry.FileOffset);
        Assert.Equal(1.5f, entry.EntryPosX);
    }

    [Fact]
    public void Parse_LegacyScriptHeader_HexBlob()
    {
        // Legacy format: header as 512 hex chars
        var headerHex = new string('F', 8) + new string('0', 504); // FFFFFFFF + zeros
        var bds = $$"""
            @version "0.26"
            @data_base 0x2B8

            entry "test" id=1 type=spawn offset=0x2B8 {
                name_raw = 7465737400000000000000000000000000000000
                reserved = 0000000000000000
                ref_id = 0
                position = (0, 0, 0)
                radius = 0
                target_refs = 0x00000000 0x00000000 0x00000000
                padding = 0000000000000000000000000000000000000000
                overlap = 000000000000000000000000
                script_block_count = 1
                script_block_offset = 0x0
                next_entry = 0x0

                script file_offset=0x500 size=0x100 {
                    header {{headerHex}}
                }
            }
            """;

        var rpj = Parser.Parse(bds);

        Assert.Single(rpj.Scripts);
        Assert.Equal(0xFFFFFFFFu, rpj.Scripts[0].Header.Sentinel);
    }

    [Fact]
    public void Parse_ReducedNoiseFormat_OmittedFieldsDefaultToZero()
    {
        // New format: omitted padding, overlap, reserved, name_raw
        var bds = """
            @version "0.26"
            @data_base 0x2B8

            entry "test" id=1 type=spawn offset=0x2B8 {
                ref_id = 0
                position = (0, 0, 0)
                radius = 0
                target_refs = 0x00000000 0x00000000 0x00000000
                script_block_count = 0
                script_block_offset = 0x0
                next_entry = 0x0
            }
            """;

        var rpj = Parser.Parse(bds);

        Assert.Single(rpj.Entries);
        var entry = rpj.Entries[0];
        // Padding region should be all zeros (default)
        var padding = entry.RawData[0x44..0x58];
        Assert.True(padding.All(b => b == 0));
    }
}
