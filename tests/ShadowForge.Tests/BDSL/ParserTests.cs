using ShadowForge.Formats.BDSL;
using ShadowForge.Formats.RPJ;
using ShadowForge.IO;
using ShadowForge.Text;

namespace ShadowForge.Tests.Bdsl;

public class ParserTests
{
    public ParserTests()
    {
        EncodingSetup.EnsureRegistered();
    }

    [Fact]
    public void Parse_SceneHeader()
    {
        var bdsl = """
            scene "TestScene" {
                version = "0.26"
                flags = 1
                area = town
                messages = "mes_bg01_01.u16"
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Equal("0.26", rpj.Header.Version);
        Assert.Equal(1u, rpj.Header.Flags);
        Assert.Equal(AreaType.Town, rpj.Sections.AreaType);
        Assert.Equal("mes_bg01_01.u16", rpj.Header.MessagePath);
    }

    [Fact]
    public void Parse_SpawnEntry()
    {
        var bdsl = """
            spawn "NPC" id=1 {
                position = (100.0, 50.0, -200.0)
                facing = 90.0
                ref_id = 3
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Single(rpj.Entries);
        var entry = rpj.Entries[0];
        Assert.Equal(1u, entry.EntryId);
        Assert.Equal(0u, entry.EntryType); // spawn = 0
        Assert.Equal(100.0f, entry.EntryPosX);
        Assert.Equal(50.0f, entry.EntryPosY);
        Assert.Equal(-200.0f, entry.EntryPosZ);
        Assert.Equal(90.0f, entry.EntryFacing);
        Assert.Equal(3u, entry.EntryRefId);
    }

    [Fact]
    public void Parse_NpcEntry_WithExtents()
    {
        var bdsl = """
            npc "Zone" id=3 {
                position = (0, 0, 0)
                extents = (10.5, 5.0, 8.0)
                yaw = 45.0
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Single(rpj.Entries);
        var entry = rpj.Entries[0];
        Assert.Equal(1u, entry.EntryType); // npc = 1
        Assert.Equal(10.5f, entry.ExtentX);
        Assert.Equal(5.0f, entry.ExtentY);
        Assert.Equal(8.0f, entry.ExtentZ);
        Assert.Equal(45.0f, entry.EntryField44);
    }

    [Fact]
    public void Parse_EnemyEntry_WithAggro()
    {
        var bdsl = """
            enemy "Mob" id=10 {
                position = (0, 0, 0)
                route = (1.0, 2.0, 3.0)
                aggro_radius = 30.0
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Single(rpj.Entries);
        var entry = rpj.Entries[0];
        Assert.Equal(3u, entry.EntryType); // enemy = 3
        Assert.Equal(1.0f, entry.ExtentX);
        Assert.Equal(2.0f, entry.ExtentY);
        Assert.Equal(3.0f, entry.ExtentZ);
        Assert.Equal(30.0f, entry.EntryField44);
    }

    [Fact]
    public void Parse_WarpEntry_WithRotation()
    {
        var bdsl = """
            warp "Exit" id=7 {
                position = (0, 0, 0)
                rotation = 90.0
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Single(rpj.Entries);
        var entry = rpj.Entries[0];
        Assert.Equal(6u, entry.EntryType); // warp = 6
        Assert.Equal(7u, entry.EntryId);
        Assert.Equal(90.0f, entry.EntryField44);
    }

    [Fact]
    public void Parse_WhenBlock_WithConditions()
    {
        var bdsl = """
            spawn "NPC" id=1 {
                position = (0, 0, 0)

                when chapter 0..59, variable[6] == 0 {
                }
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Single(rpj.Scripts);
        var block = rpj.Scripts[0];
        var header = block.Header;
        Assert.Equal(0u, header.ChapterMin);
        Assert.Equal(59u, header.ChapterMax);
        Assert.False(header.IsUnconditional);

        // First condition: variable[6] == 0
        var cond = header.GetCondition(0);
        Assert.Equal(2u, cond.Type);    // variable
        Assert.Equal(6u, cond.Operand);
        Assert.Equal(0u, cond.Op);      // ==
        Assert.Equal(0u, cond.Value);
    }

    [Fact]
    public void Parse_WhenAll()
    {
        var bdsl = """
            spawn "NPC" id=1 {
                when all {
                }
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Single(rpj.Scripts);
        var block = rpj.Scripts[0];
        Assert.True(block.Header.IsUnconditional);
        Assert.Equal(0xFFFFFFFFu, block.Header.ChapterMin);
    }

    [Fact]
    public void Parse_MetadataDirectives()
    {
        var bdsl = """
            spawn "NPC" id=1 {
                when all {
                    @block_type npc
                    @behavior default
                    @encounter normal
                    @interaction_radius 30.0
                    @render visible, collision
                }
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Single(rpj.Scripts);
        var header = rpj.Scripts[0].Header;
        Assert.Equal(2u, header.BlockType);             // npc = 2
        Assert.Equal(1, header.BehaviorMode);           // default = 1
        Assert.Equal(1u, header.EncounterMode);         // normal = 1
        Assert.Equal(30.0f, header.InteractionRadius);
        Assert.Equal(0x10000001u, header.RenderFlags);  // visible(0x1) | collision(0x10000000)
    }

    [Fact]
    public void Parse_Waypoint()
    {
        var bdsl = """
            waypoint 1 at (100.0, 50.0, -200.0) rotation=90.0 {
                targets = [2, 3, 0]
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Single(rpj.Waypoints);
        var wp = rpj.Waypoints[0];
        Assert.Equal(1u, wp.Id);
        Assert.Equal(0u, wp.WaypointType); // default waypoint type
        Assert.Equal(100.0f, wp.PosX);
        Assert.Equal(50.0f, wp.PosY);
        Assert.Equal(-200.0f, wp.PosZ);
        Assert.Equal(90.0f, wp.Rotation);
        Assert.Equal(2u, wp.Target0);
        Assert.Equal(3u, wp.Target1);
        Assert.Equal(0u, wp.Target2);
    }

    [Fact]
    public void Parse_AliasComment()
    {
        // Opcode 5097 is an alias for set_variable (canonical 5003)
        // When # op=5097 is present, the instruction should use opcode 5097
        var bdsl = """
            spawn "NPC" id=1 {
                when all {
                    set_variable 0x6, 0x0, 0x0, 0x1    # op=5097
                }
            }
            """;

        var rpj = Parser.Parse(bdsl);

        Assert.Single(rpj.Scripts);
        var block = rpj.Scripts[0];
        Assert.Single(block.Elements);

        var instr = Assert.IsType<ScriptInstruction>(block.Elements[0]);
        Assert.Equal(5097u, instr.Opcode);
        Assert.Equal(4, instr.RawParams.Length);
        Assert.Equal(0x6u, instr.RawParams[0]);
        Assert.Equal(0x0u, instr.RawParams[1]);
        Assert.Equal(0x0u, instr.RawParams[2]);
        Assert.Equal(0x1u, instr.RawParams[3]);
    }
}
