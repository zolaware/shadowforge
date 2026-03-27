using ShadowForge.IO;
using ShadowForge.Text;

namespace ShadowForge.Formats.RPJ;

public enum AreaType : uint
{
    Town = 1,
    Indoor = 2,
    Dungeon = 3,
    World = 4,
    Cube = 5,
}

public class SceneFile
{
    public FileHeader Header { get; set; } = new();
    public SectionTable Sections { get; set; } = new();
    public List<Entry> Entries { get; set; } = new();
    public List<ScriptBlock> Scripts { get; set; } = new();
    public List<Waypoint> Waypoints { get; set; } = new();
    public List<RawGap> Gaps { get; set; } = new();
    public int OriginalFileSize { get; set; }
}

public class FileHeader
{
    public byte[] RawData { get; set; } = new byte[0x270];

    public string Version => System.Text.Encoding.ASCII.GetString(RawData, 0, 4).TrimEnd('\0');
    public uint Flags => BigEndian.ReadUInt32(RawData, 0x08);
    public string SceneName => ShiftJisHelper.Decode(RawData, 0x0C, 64);
    public string Filename => ShiftJisHelper.Decode(RawData, 0x4C, 28);
    public string MessagePath => ShiftJisHelper.Decode(RawData, 0x170, 256);

    public int BuildPathWritePos { get; set; } = 0x68;
    public bool BuildPathsCleared { get; set; }
}

public class SectionTable
{
    public uint Reserved0 { get; set; }
    public AreaType AreaType { get; set; }
    public uint TotalScriptCmds { get; set; }
    public uint[] Reserved1 { get; set; } = new uint[4];
    public uint EntryCount { get; set; }
    public uint[] Reserved2 { get; set; } = new uint[4];
    public uint Reserved3 { get; set; }
    public uint Section1Offset { get; set; }
    public uint Section2Offset { get; set; }
    public uint DataBaseOffset { get; set; }
    public uint Section3Offset { get; set; }
    public uint Section4Offset { get; set; }
}

public class Waypoint
{
    public byte[] RawData { get; set; } = new byte[0x40];
    public int FileOffset { get; set; }

    public uint Id => BigEndian.ReadUInt32(RawData, 0x00);
    public uint WaypointType => BigEndian.ReadUInt32(RawData, 0x04);
    public uint ConditionFlags => BigEndian.ReadUInt32(RawData, 0x08);
    public uint ConditionValue => BigEndian.ReadUInt32(RawData, 0x0C);
    public float PosX => BigEndian.ReadFloat(RawData, 0x10);
    public float PosY => BigEndian.ReadFloat(RawData, 0x14);
    public float PosZ => BigEndian.ReadFloat(RawData, 0x18);
    public float Rotation => BigEndian.ReadFloat(RawData, 0x1C);
    public byte[] RefData => RawData[0x20..0x30];
    public uint Target0 => BigEndian.ReadUInt32(RawData, 0x30);
    public uint Target1 => BigEndian.ReadUInt32(RawData, 0x34);
    public uint Target2 => BigEndian.ReadUInt32(RawData, 0x38);
    public uint NextOffset => BigEndian.ReadUInt32(RawData, 0x3C);

    public uint ConditionType => ConditionFlags & 0x0F;
    public uint ConditionOp => (ConditionFlags >> 4) & 0x0F;

    public bool IsResource => WaypointType == 5;
    public bool IsSpawn => WaypointType == 6;

    public void WriteBE(int offset, uint value) => BigEndian.WriteUInt32(RawData, offset, value);
    public void WriteBEFloat(int offset, float value) => BigEndian.WriteFloat(RawData, offset, value);
}

public class RawGap
{
    public int FileOffset { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
