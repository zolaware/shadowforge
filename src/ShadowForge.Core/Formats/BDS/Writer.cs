using System.Globalization;
using System.Text;
using ShadowForge.Formats.RPJ;
using ShadowForge.IO;
using ShadowForge.Text;

namespace ShadowForge.Formats.BDS;

public static class Writer
{
    public static string Write(SceneFile rpj)
    {
        var sb = new StringBuilder();

        WriteHeader(sb, rpj);
        sb.AppendLine();

        if (rpj.Entries.Count > 0)
        {
            sb.AppendLine("# --- Entries ---");
            sb.AppendLine();
            foreach (var entry in rpj.Entries)
                WriteEntry(sb, entry);
        }

        if (rpj.Waypoints.Count > 0)
        {
            sb.AppendLine("# --- Waypoints (Section 4) ---");
            sb.AppendLine();
            foreach (var wp in rpj.Waypoints)
                WriteWaypoint(sb, wp);
        }

        if (rpj.Gaps.Count > 0)
        {
            sb.AppendLine("# --- Raw data gaps ---");
            sb.AppendLine();
            foreach (var gap in rpj.Gaps)
                WriteGap(sb, gap);
        }

        return sb.ToString();
    }

    public static void Write(SceneFile rpj, string path)
    {
        var text = Write(rpj);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static void WriteHeader(StringBuilder sb, SceneFile rpj)
    {
        var h = rpj.Header;
        var st = rpj.Sections;

        sb.AppendLine($"@version \"{h.Version}\"");
        sb.AppendLine($"@flags {h.Flags}");
        sb.AppendLine($"@scene \"{EscapeString(h.SceneName)}\"");
        sb.AppendLine($"@filename \"{EscapeString(h.Filename)}\"");
        sb.AppendLine($"@area {AreaTypeName(st.AreaType)}");

        sb.AppendLine($"@message \"{EscapeString(h.MessagePath)}\"");
        foreach (var bp in DecodeBuildPaths(h.RawData, 0x68, 264))
            sb.AppendLine($"@build_path \"{EscapeString(bp)}\"");

        sb.AppendLine($"@total_cmds {st.TotalScriptCmds}");
        sb.AppendLine($"@entry_count {st.EntryCount}");
        sb.AppendLine($"@file_size {rpj.OriginalFileSize}");

        sb.AppendLine($"@section1 0x{st.Section1Offset:X}");
        sb.AppendLine($"@section2 0x{st.Section2Offset:X}");
        sb.AppendLine($"@section3 0x{st.Section3Offset:X}");
        sb.AppendLine($"@section4 0x{st.Section4Offset:X}");
        sb.AppendLine($"@data_base 0x{st.DataBaseOffset:X}");

        sb.AppendLine($"@reserved0 0x{st.Reserved0:X8}");
        var r1 = string.Join(" ", st.Reserved1.Select(v => $"0x{v:X8}"));
        sb.AppendLine($"@reserved1 {r1}");
        var r2 = string.Join(" ", st.Reserved2.Select(v => $"0x{v:X8}"));
        sb.AppendLine($"@reserved2 {r2}");
        sb.AppendLine($"@reserved3 0x{st.Reserved3:X8}");
    }

    private static void WriteEntry(StringBuilder sb, Entry entry)
    {
        string entryTypeName = entry.EntryType switch
        {
            0 => "spawn", 1 => "npc", 2 => "zone", 3 => "enemy",
            4 => "link", 5 => "entity", 6 => "event",
            _ => entry.EntryType.ToString(),
        };

        sb.AppendLine($"entry \"{EscapeString(entry.EntryName)}\" id={entry.EntryId} type={entryTypeName} offset=0x{entry.FileOffset:X} {{");

        // name_raw: only emit if it differs from the entry name zero-padded
        var nameRaw = entry.EntryNameRaw;
        if (!NameRawMatchesName(entry.EntryName, nameRaw))
            sb.AppendLine($"    name_raw = {Convert.ToHexString(nameRaw)}");

        // reserved: only emit if non-zero
        var reserved = entry.RawData[0x18..0x20];
        if (reserved.Any(b => b != 0))
            sb.AppendLine($"    reserved = {Convert.ToHexString(reserved)}");

        sb.AppendLine($"    ref_id = {entry.EntryRefId}");
        sb.AppendLine($"    position = ({FormatFloat(entry.EntryPosX)}, {FormatFloat(entry.EntryPosY)}, {FormatFloat(entry.EntryPosZ)})");
        sb.AppendLine($"    radius = {FormatFloat(entry.EntryRadius)}");
        sb.AppendLine($"    target_refs = 0x{entry.EntryTargetRef0:X8} 0x{entry.EntryTargetRef1:X8} 0x{entry.EntryTargetRef2:X8}");

        // padding: only emit if non-zero
        var padding = entry.RawData[0x44..0x58];
        if (padding.Any(b => b != 0))
            sb.AppendLine($"    padding = {Convert.ToHexString(padding)}");

        // overlap: only emit if non-zero
        var overlap = entry.RawData[0x58..0x64];
        if (overlap.Any(b => b != 0))
            sb.AppendLine($"    overlap = {Convert.ToHexString(overlap)}");

        sb.AppendLine($"    script_block_count = {entry.EntryScriptBlockCount}");
        sb.AppendLine($"    script_block_offset = 0x{entry.EntryScriptBlockOffset:X}");
        sb.AppendLine($"    next_entry = 0x{entry.EntryNextOffset:X}");

        WriteScriptBlocks(sb, entry);

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static bool NameRawMatchesName(string name, byte[] nameRaw)
    {
        // Returns true if the raw bytes are just the name zero-padded (no extra data to preserve)
        if (string.IsNullOrEmpty(name)) return nameRaw.All(b => b == 0);
        var encoded = ShiftJisHelper.Encode(name);
        if (encoded.Length > nameRaw.Length) return false;

        for (int i = 0; i < nameRaw.Length; i++)
        {
            byte expected = i < encoded.Length ? encoded[i] : (byte)0;
            if (nameRaw[i] != expected) return false; // differs — raw has extra data
        }
        return true; // matches — safe to omit
    }

    private static void WriteScriptBlocks(StringBuilder sb, Entry entry)
    {
        foreach (var script in entry.ScriptBlocks)
        {
            sb.AppendLine();
            sb.AppendLine($"    script file_offset=0x{script.FileOffset:X} size=0x{script.FileSize:X} {{");

            var hdr = script.Header;
            // Emit decoded header fields instead of hex blob
            sb.AppendLine($"        sentinel = 0x{hdr.Sentinel:X8}");
            sb.AppendLine($"        bytecode_size = 0x{hdr.BytecodeSize:X}");
            sb.AppendLine($"        param_size = 0x{hdr.ParamDataSize:X}");
            sb.AppendLine($"        next_block = 0x{hdr.NextBlockOffset:X}");

            // Emit unknown regions only if non-zero
            var unknownMiddle = hdr.UnknownMiddle;
            if (unknownMiddle != null)
                sb.AppendLine($"        header_middle = {Convert.ToHexString(unknownMiddle)}");

            var unknownTail = hdr.UnknownTail;
            if (unknownTail != null)
                sb.AppendLine($"        header_tail = {Convert.ToHexString(unknownTail)}");

            foreach (var elem in script.Elements)
            {
                if (elem is ScriptInstruction instr)
                    WriteInstruction(sb, instr);
                else if (elem is ScriptData rawData)
                    sb.AppendLine($"        data {Convert.ToHexString(rawData.Bytes)}");
            }
            if (script.ParamData.Length > 0)
                sb.AppendLine($"        param_data {Convert.ToHexString(script.ParamData)}");
            sb.AppendLine("    }");
        }
    }

    private static void WriteInstruction(StringBuilder sb, ScriptInstruction instr)
    {
        string name = Opcodes.GetName(instr.Opcode);
        var paramStr = string.Join(" ", instr.RawParams.Select(p => $"0x{p:X8}"));

        if (instr.RawParams.Length > 0)
            sb.AppendLine($"        {name} {paramStr}");
        else
            sb.AppendLine($"        {name}");
    }

    private static void WriteWaypoint(StringBuilder sb, Waypoint wp)
    {
        uint index = BigEndian.ReadUInt32(wp.RawData, 0x00);
        sb.AppendLine($"waypoint 0x{index:X} offset=0x{wp.FileOffset:X} {{");
        sb.AppendLine($"    raw = {Convert.ToHexString(wp.RawData)}");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteGap(StringBuilder sb, RawGap gap)
    {
        string hex = Convert.ToHexString(gap.Data);
        sb.Append($"@raw 0x{gap.FileOffset:X} ");
        const int chunkSize = 128;
        for (int i = 0; i < hex.Length; i += chunkSize)
        {
            if (i > 0) sb.Append($"\n@raw+ ");
            sb.Append(hex[i..Math.Min(i + chunkSize, hex.Length)]);
        }
        sb.AppendLine();
    }

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            // Emit as big-endian hex to preserve exact bit pattern
            uint raw = BitConverter.SingleToUInt32Bits(value);
            return $"0x{raw:X8}";
        }
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string AreaTypeName(AreaType t) => t switch
    {
        AreaType.Town => "town",
        AreaType.Indoor => "indoor",
        AreaType.Dungeon => "dungeon",
        AreaType.World => "world",
        AreaType.Cube => "cube",
        _ => $"type_{(uint)t}",
    };

    private static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static List<string> DecodeBuildPaths(byte[] header, int offset, int length)
    {
        var paths = new List<string>();
        int pos = offset;
        int end = offset + length;
        while (pos < end)
        {
            int nullPos = Array.IndexOf(header, (byte)0, pos, end - pos);
            if (nullPos < 0) nullPos = end;
            if (nullPos > pos)
                paths.Add(ShiftJisHelper.Encoding.GetString(header, pos, nullPos - pos));
            pos = nullPos + 1;
            while (pos < end && header[pos] == 0) pos++;
        }
        return paths;
    }

}
