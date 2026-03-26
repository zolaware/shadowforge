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
}

public class RawGap
{
    public int FileOffset { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
