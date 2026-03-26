using ShadowForge.IO;

namespace ShadowForge.Formats.RPJ;

public static class Writer
{
    public static byte[] Write(SceneFile rpj)
    {
        int fileSize = rpj.OriginalFileSize > 0 ? rpj.OriginalFileSize : ComputeFileSize(rpj);
        var data = new byte[fileSize];

        Array.Copy(rpj.Header.RawData, 0, data, 0, Math.Min(rpj.Header.RawData.Length, Reader.HeaderSize));
        WriteSectionTable(data, Reader.HeaderSize, rpj.Sections);

        foreach (var entry in rpj.Entries)
            Array.Copy(entry.RawData, 0, data, entry.FileOffset, Reader.EntrySize);

        foreach (var script in rpj.Scripts)
        {
            int pos = script.FileOffset;

            if (script.HeaderData.Length > 0)
            {
                Array.Copy(script.HeaderData, 0, data, pos, Math.Min(script.HeaderData.Length, 0x100));
                pos += 0x100;
            }

            foreach (var elem in script.Elements)
            {
                if (elem is ScriptInstruction instr)
                {
                    BigEndian.WriteUInt32(data, pos, instr.Opcode);
                    BigEndian.WriteUInt32(data, pos + 4, instr.Size);
                    for (int i = 0; i < instr.RawParams.Length; i++)
                        BigEndian.WriteUInt32(data, pos + 8 + i * 4, instr.RawParams[i]);
                    pos += (int)instr.Size;
                }
                else if (elem is ScriptData raw)
                {
                    Array.Copy(raw.Bytes, 0, data, pos, raw.Bytes.Length);
                    pos += raw.Bytes.Length;
                }
            }

            if (script.ParamData.Length > 0)
            {
                Array.Copy(script.ParamData, 0, data, pos, script.ParamData.Length);
                pos += script.ParamData.Length;
            }
        }

        foreach (var wp in rpj.Waypoints)
            Array.Copy(wp.RawData, 0, data, wp.FileOffset, 0x40);

        foreach (var gap in rpj.Gaps)
            Array.Copy(gap.Data, 0, data, gap.FileOffset, gap.Data.Length);

        return data;
    }

    public static void Write(SceneFile rpj, string path) => File.WriteAllBytes(path, Write(rpj));

    private static void WriteSectionTable(byte[] data, int offset, SectionTable st)
    {
        int pos = offset;
        BigEndian.WriteUInt32(data, pos, st.Reserved0); pos += 4;
        BigEndian.WriteUInt32(data, pos, (uint)st.AreaType); pos += 4;
        BigEndian.WriteUInt32(data, pos, st.TotalScriptCmds); pos += 4;
        for (int i = 0; i < 4; i++) { BigEndian.WriteUInt32(data, pos, st.Reserved1[i]); pos += 4; }
        BigEndian.WriteUInt32(data, pos, st.EntryCount); pos += 4;
        for (int i = 0; i < 4; i++) { BigEndian.WriteUInt32(data, pos, st.Reserved2[i]); pos += 4; }
        BigEndian.WriteUInt32(data, pos, st.Reserved3); pos += 4;
        BigEndian.WriteUInt32(data, pos, st.Section1Offset); pos += 4;
        BigEndian.WriteUInt32(data, pos, st.Section2Offset); pos += 4;
        BigEndian.WriteUInt32(data, pos, st.DataBaseOffset); pos += 4;
        BigEndian.WriteUInt32(data, pos, st.Section3Offset); pos += 4;
        BigEndian.WriteUInt32(data, pos, st.Section4Offset);
    }

    private static int ComputeFileSize(SceneFile rpj)
    {
        int max = Reader.DataBaseOffset;
        foreach (var n in rpj.Entries)
            max = Math.Max(max, n.FileOffset + Reader.EntrySize);
        foreach (var s in rpj.Scripts)
            max = Math.Max(max, s.FileOffset + s.FileSize);
        foreach (var w in rpj.Waypoints)
            max = Math.Max(max, w.FileOffset + 0x40);
        foreach (var g in rpj.Gaps)
            max = Math.Max(max, g.FileOffset + g.Data.Length);
        return max;
    }

}
