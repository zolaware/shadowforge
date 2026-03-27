using ShadowForge.IO;
using ShadowForge.Text;

namespace ShadowForge.Formats.RPJ;

public class Entry
{
    public int FileOffset { get; set; }
    public byte[] RawData { get; set; } = new byte[0x70];
    public List<ScriptBlock> ScriptBlocks { get; set; } = new();

    public uint EntryId => BigEndian.ReadUInt32(RawData, 0x00);
    public byte[] EntryNameRaw => RawData[0x04..0x18];
    public string EntryName
    {
        get
        {
            var raw = EntryNameRaw;
            int end = Array.IndexOf(raw, (byte)0);
            if (end < 0) end = raw.Length;
            if (end == 0) return "";
            return ShiftJisHelper.Encoding.GetString(raw, 0, end);
        }
    }
    public uint EntryRefId => BigEndian.ReadUInt32(RawData, 0x20);
    public uint EntryType => BigEndian.ReadUInt32(RawData, 0x24);
    public float EntryPosX => BigEndian.ReadFloat(RawData, 0x28);
    public float EntryPosY => BigEndian.ReadFloat(RawData, 0x2C);
    public float EntryPosZ => BigEndian.ReadFloat(RawData, 0x30);
    public float EntryRadius => BigEndian.ReadFloat(RawData, 0x34);
    public uint EntryTargetRef0 => BigEndian.ReadUInt32(RawData, 0x38);
    public uint EntryTargetRef1 => BigEndian.ReadUInt32(RawData, 0x3C);
    public uint EntryTargetRef2 => BigEndian.ReadUInt32(RawData, 0x40);

    // Semantic name: +0x34 is facing angle, not radius
    public float EntryFacing => BigEndian.ReadFloat(RawData, 0x34);

    // Type 1 (npc): trigger box half-extents as floats
    public float ExtentX => BigEndian.ReadFloat(RawData, 0x38);
    public float ExtentY => BigEndian.ReadFloat(RawData, 0x3C);
    public float ExtentZ => BigEndian.ReadFloat(RawData, 0x40);

    // +0x44: type-dependent (npc yaw, enemy aggro radius, warp rotation)
    public float EntryField44 => BigEndian.ReadFloat(RawData, 0x44);

    public uint EntryScriptBlockCount => BigEndian.ReadUInt32(RawData, 0x64);
    public uint EntryScriptBlockOffset => BigEndian.ReadUInt32(RawData, 0x68);
    public uint EntryNextOffset => BigEndian.ReadUInt32(RawData, 0x6C);

    public void WriteBE(int offset, uint value) => BigEndian.WriteUInt32(RawData, offset, value);
    public void WriteBEFloat(int offset, float value) => BigEndian.WriteFloat(RawData, offset, value);
    public uint ReadBERaw(int offset) => BigEndian.ReadUInt32(RawData, offset);
}
