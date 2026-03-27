// P:\shadowforge\tests\ShadowForge.Tests\BDSL\WriterTests.cs
using BDSL = ShadowForge.Formats.BDSL;
using ShadowForge.Formats.RPJ;
using ShadowForge.IO;
using ShadowForge.Text;

namespace ShadowForge.Tests.Bdsl;

public class WriterTests
{
    public WriterTests()
    {
        EncodingSetup.EnsureRegistered();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SceneFile MinimalScene(string sceneName = "TestScene", AreaType area = AreaType.Town)
    {
        var rpj = new SceneFile();
        // Write version "0.26" at offset 0 of header raw data
        rpj.Header.RawData[0] = (byte)'0';
        rpj.Header.RawData[1] = (byte)'.';
        rpj.Header.RawData[2] = (byte)'2';
        rpj.Header.RawData[3] = (byte)'6';
        // Write scene name (ASCII for simplicity in tests)
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(sceneName);
        Array.Copy(nameBytes, 0, rpj.Header.RawData, 0x0C, Math.Min(nameBytes.Length, 63));
        rpj.Sections.AreaType = area;
        return rpj;
    }

    private static Entry MakeEntry(uint id, uint type, string name = "TestEntry")
    {
        var entry = new Entry();
        BigEndian.WriteUInt32(entry.RawData, 0x00, id);
        BigEndian.WriteUInt32(entry.RawData, 0x24, type);
        var nameBytes = ShiftJisHelper.Encode(name);
        Array.Copy(nameBytes, 0, entry.RawData, 0x04, Math.Min(nameBytes.Length, 19));
        return entry;
    }

    private static ScriptBlock MakeUnconditionalBlock()
    {
        var block = new ScriptBlock();
        // ChapterMin = 0xFFFFFFFF means unconditional
        BigEndian.WriteUInt32(block.HeaderData, 0x00, 0xFFFFFFFF);
        BigEndian.WriteUInt32(block.HeaderData, 0x04, 0xFFFFFFFF);
        return block;
    }

    private static ScriptBlock MakeConditionalBlock(uint chapterMin, uint chapterMax)
    {
        var block = new ScriptBlock();
        BigEndian.WriteUInt32(block.HeaderData, 0x00, chapterMin);
        BigEndian.WriteUInt32(block.HeaderData, 0x04, chapterMax);
        return block;
    }

    // -------------------------------------------------------------------------
    // Test: scene header
    // -------------------------------------------------------------------------

    [Fact]
    public void Write_SceneHeader_ContainsSceneBlock()
    {
        var rpj = MinimalScene("TestTown", AreaType.Town);
        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("scene \"TestTown\" {", output);
        Assert.Contains("version = \"0.26\"", output);
        Assert.Contains("area = town", output);
        Assert.Contains("}", output);
    }

    [Fact]
    public void Write_SceneHeader_DungeonArea()
    {
        var rpj = MinimalScene("DungeonMap", AreaType.Dungeon);
        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("area = dungeon", output);
    }

    [Fact]
    public void Write_SceneHeader_OmitsFlagsWhenZero()
    {
        var rpj = MinimalScene();
        // Flags at offset 0x08 - leave as zero (default)
        var output = BDSL.Writer.Write(rpj);

        Assert.DoesNotContain("flags =", output);
    }

    [Fact]
    public void Write_SceneHeader_EmitsFlagsWhenNonZero()
    {
        var rpj = MinimalScene();
        BigEndian.WriteUInt32(rpj.Header.RawData, 0x08, 1);
        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("flags = 1", output);
    }

    [Fact]
    public void Write_SceneHeader_EmitsBuildPaths()
    {
        var rpj = MinimalScene();
        // Write a build path at offset 0x68
        var pathBytes = System.Text.Encoding.ASCII.GetBytes("D:\\BD_PLAN_VSS\\map\\");
        Array.Copy(pathBytes, 0, rpj.Header.RawData, 0x68, pathBytes.Length);
        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("build_path =", output);
    }

    // -------------------------------------------------------------------------
    // Test: entry blocks - spawn type
    // -------------------------------------------------------------------------

    [Fact]
    public void Write_EntryBlock_SpawnType()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(18, 0, "SpawnEntry"); // type 0 = spawn
        BigEndian.WriteFloat(entry.RawData, 0x28, -285.83398f);
        BigEndian.WriteFloat(entry.RawData, 0x2C, 295.6238f);
        BigEndian.WriteFloat(entry.RawData, 0x30, -901.97534f);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("spawn \"SpawnEntry\" id=18", output);
        Assert.Contains("position =", output);
        Assert.Contains("-285.83398", output);
    }

    [Fact]
    public void Write_EntryBlock_CorrectKeywords()
    {
        uint[] types = { 0, 1, 2, 3, 4, 5, 6 };
        string[] expected = { "spawn", "npc", "zone", "enemy", "link", "entity", "warp" };

        for (int i = 0; i < types.Length; i++)
        {
            var rpj = MinimalScene();
            var entry = MakeEntry((uint)(i + 1), types[i], "E");
            rpj.Entries.Add(entry);
            var output = BDSL.Writer.Write(rpj);
            Assert.Contains(expected[i], output);
        }
    }

    [Fact]
    public void Write_EntryBlock_OmitsZeroPosition()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "NoPos");
        // RawData defaults to zeros - position is (0,0,0)
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.DoesNotContain("position =", output);
    }

    [Fact]
    public void Write_EntryBlock_EmitsOffsetComment()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "OffsetEntry");
        entry.FileOffset = 0x408;
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("# offset=0x408", output);
    }

    // -------------------------------------------------------------------------
    // Test: entry blocks - npc type with extents/yaw
    // -------------------------------------------------------------------------

    [Fact]
    public void Write_EntryBlock_NpcType_WithExtents()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(5, 1, "NpcEntry"); // type 1 = npc
        BigEndian.WriteFloat(entry.RawData, 0x38, 10.0f);
        BigEndian.WriteFloat(entry.RawData, 0x3C, 5.0f);
        BigEndian.WriteFloat(entry.RawData, 0x40, 10.0f);
        BigEndian.WriteFloat(entry.RawData, 0x44, 45.0f); // yaw
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("npc \"NpcEntry\" id=5", output);
        Assert.Contains("extents = (10", output);
        Assert.Contains("yaw = 45", output);
    }

    [Fact]
    public void Write_EntryBlock_EnemyType_WithRouteAndAggroRadius()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(7, 3, "EnemyEntry"); // type 3 = enemy
        BigEndian.WriteFloat(entry.RawData, 0x38, 100.0f);
        BigEndian.WriteFloat(entry.RawData, 0x3C, 0.0f);
        BigEndian.WriteFloat(entry.RawData, 0x40, 200.0f);
        BigEndian.WriteFloat(entry.RawData, 0x44, 50.0f); // aggro_radius
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("enemy \"EnemyEntry\" id=7", output);
        Assert.Contains("route = (100", output);
        Assert.Contains("aggro_radius = 50", output);
    }

    [Fact]
    public void Write_EntryBlock_WarpType_WithRotation()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(9, 6, "WarpEntry"); // type 6 = warp
        BigEndian.WriteFloat(entry.RawData, 0x44, 90.0f); // rotation
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("warp \"WarpEntry\" id=9", output);
        Assert.Contains("rotation = 90", output);
    }

    // -------------------------------------------------------------------------
    // Test: script blocks - when syntax
    // -------------------------------------------------------------------------

    [Fact]
    public void Write_ScriptBlock_WhenAll()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("when all {", output);
    }

    [Fact]
    public void Write_ScriptBlock_WhenSyntax_ChapterRange()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeConditionalBlock(0, 59);
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("when chapter 0..59 {", output);
    }

    [Fact]
    public void Write_ScriptBlock_WhenSyntax_WithCondition()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeConditionalBlock(0, 59);
        // Set condition[0]: type=2 (variable), operand=6, op=0 (==), value=0
        BigEndian.WriteUInt32(block.HeaderData, 0x08 + 0 * 16 + 0, 2);  // type=variable
        BigEndian.WriteUInt32(block.HeaderData, 0x08 + 0 * 16 + 4, 6);  // operand=6
        BigEndian.WriteUInt32(block.HeaderData, 0x08 + 0 * 16 + 8, 0);  // op==
        BigEndian.WriteUInt32(block.HeaderData, 0x08 + 0 * 16 + 12, 0); // value=0
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("when chapter 0..59", output);
        Assert.Contains("variable[6] == 0", output);
    }

    [Fact]
    public void Write_ScriptBlock_WhenSyntax_FileOffsetComment()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        block.FileOffset = 0x2458;
        block.FileSize = 0x1C0;
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("file_offset=0x2458", output);
        Assert.Contains("size=0x1C0", output);
    }

    // -------------------------------------------------------------------------
    // Test: metadata (@-prefixed fields)
    // -------------------------------------------------------------------------

    [Fact]
    public void Write_ScriptBlock_MetadataFields_BlockType()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        BigEndian.WriteUInt32(block.HeaderData, 0xE0, 2); // block_type = npc
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("@block_type npc", output);
    }

    [Fact]
    public void Write_ScriptBlock_MetadataFields_RenderFlags()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        // Set visible (bit0) + collision (bit28)
        uint flags = (1u << 0) | (1u << 28);
        BigEndian.WriteUInt32(block.HeaderData, 0xD4, flags);
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("@render visible, collision", output);
    }

    [Fact]
    public void Write_ScriptBlock_MetadataFields_Behavior()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        BigEndian.WriteInt32(block.HeaderData, 0xD8, 1); // behavior = default
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("@behavior default", output);
    }

    [Fact]
    public void Write_ScriptBlock_MetadataFields_OmittedWhenZero()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        // Leave all metadata fields as zero
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.DoesNotContain("@block_type", output);
        Assert.DoesNotContain("@render", output);
        Assert.DoesNotContain("@behavior", output);
    }

    [Fact]
    public void Write_ScriptBlock_MetadataFields_EncounterMode()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        BigEndian.WriteUInt32(block.HeaderData, 0x94, 1); // encounter = normal
        BigEndian.WriteFloat(block.HeaderData, 0x98, 15.5f);
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("@encounter normal", output);
        Assert.Contains("@encounter_range 15.5", output);
    }

    // -------------------------------------------------------------------------
    // Test: instructions
    // -------------------------------------------------------------------------

    [Fact]
    public void Write_Instruction_KnownOpcode()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        block.Elements.Add(new ScriptInstruction
        {
            Opcode = 5001, // show_message
            RawParams = new uint[] { 0x154, 0 }
        });
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        // show_message with trimmed trailing zero
        Assert.Contains("show_message 0x154", output);
    }

    [Fact]
    public void Write_Instruction_AliasOpcode_EmitsComment()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        block.Elements.Add(new ScriptInstruction
        {
            Opcode = 5067, // alias for show_message
            RawParams = new uint[] { 0x1 }
        });
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("show_message", output);
        Assert.Contains("# op=5067", output);
    }

    [Fact]
    public void Write_Instruction_TrimsTrailingZeroParams()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        block.Elements.Add(new ScriptInstruction
        {
            Opcode = 5003, // set_variable
            RawParams = new uint[] { 0x6, 0x0, 0x0, 0x1, 0x0, 0x0 }
        });
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        // Should have params up to 0x1 but not trailing zeros
        Assert.Contains("set_variable 0x6, 0x0, 0x0, 0x1", output);
        // Ensure trailing zeros are not emitted after the last non-zero
        Assert.DoesNotContain("0x1, 0x0, 0x0", output);
    }

    [Fact]
    public void Write_Instruction_RawData_EmitsRawKeyword()
    {
        var rpj = MinimalScene();
        var entry = MakeEntry(1, 0, "E");
        var block = MakeUnconditionalBlock();
        block.Elements.Add(new ScriptData { Bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } });
        entry.ScriptBlocks.Add(block);
        rpj.Entries.Add(entry);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("raw DEADBEEF", output);
    }

    // -------------------------------------------------------------------------
    // Test: waypoints
    // -------------------------------------------------------------------------

    [Fact]
    public void Write_Waypoint_Basic()
    {
        var rpj = MinimalScene();
        var wp = new Waypoint();
        BigEndian.WriteUInt32(wp.RawData, 0x00, 1);     // id
        BigEndian.WriteUInt32(wp.RawData, 0x04, 0);     // type
        BigEndian.WriteFloat(wp.RawData, 0x10, 100.0f); // posX
        BigEndian.WriteFloat(wp.RawData, 0x14, 50.0f);  // posY
        BigEndian.WriteFloat(wp.RawData, 0x18, -200.0f);// posZ
        BigEndian.WriteFloat(wp.RawData, 0x1C, 90.0f);  // rotation
        rpj.Waypoints.Add(wp);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("waypoint 1 at (100", output);
        Assert.Contains("-200", output);
        Assert.Contains("rotation=90", output);
    }

    [Fact]
    public void Write_Waypoint_WithTargets()
    {
        var rpj = MinimalScene();
        var wp = new Waypoint();
        BigEndian.WriteUInt32(wp.RawData, 0x00, 3);  // id
        BigEndian.WriteUInt32(wp.RawData, 0x30, 2);  // target0
        BigEndian.WriteUInt32(wp.RawData, 0x34, 3);  // target1
        BigEndian.WriteUInt32(wp.RawData, 0x38, 0);  // target2
        rpj.Waypoints.Add(wp);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("targets = [2, 3, 0]", output);
    }

    [Fact]
    public void Write_Waypoint_OmitsRotationWhenZero()
    {
        var rpj = MinimalScene();
        var wp = new Waypoint();
        BigEndian.WriteUInt32(wp.RawData, 0x00, 5); // id
        // rotation left as zero
        rpj.Waypoints.Add(wp);

        var output = BDSL.Writer.Write(rpj);

        Assert.Contains("waypoint 5 at", output);
        Assert.DoesNotContain("rotation=", output);
    }

    [Fact]
    public void Write_Waypoint_OmitsTargetsWhenAllZero()
    {
        var rpj = MinimalScene();
        var wp = new Waypoint();
        BigEndian.WriteUInt32(wp.RawData, 0x00, 2); // id
        // targets left as zero
        rpj.Waypoints.Add(wp);

        var output = BDSL.Writer.Write(rpj);

        Assert.DoesNotContain("targets =", output);
    }
}
