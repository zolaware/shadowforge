using System.Globalization;
using System.Text;
using ShadowForge.Formats.RPJ;
using ShadowForge.IO;
using ShadowForge.Text;

namespace ShadowForge.Formats.BDS;

public static class Parser
{
    public static SceneFile Parse(string text)
    {
        EncodingSetup.EnsureRegistered();
        var rpj = new SceneFile();
        rpj.Header = new FileHeader();
        rpj.Sections = new SectionTable();

        var lines = text.Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = StripComment(line).Trim();

            if (string.IsNullOrEmpty(trimmed)) { i++; continue; }

            if (trimmed.StartsWith("@"))
                i = ParseDirective(rpj, lines, i);
            else if (trimmed.StartsWith("entry "))
                i = ParseEntry(rpj, lines, i);
            else if (trimmed.StartsWith("waypoint "))
                i = ParseWaypoint(rpj, lines, i);
            else
                i++;
        }

        return rpj;
    }

    public static SceneFile ParseFile(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return Parse(text);
    }

    private static int ParseDirective(SceneFile rpj, string[] lines, int i)
    {
        var line = StripComment(lines[i].TrimEnd('\r')).Trim();
        var parts = SplitDirective(line);
        string key = parts[0];
        string value = parts.Length > 1 ? parts[1] : "";

        switch (key)
        {
            case "@version":
                WriteHeaderASCII(rpj.Header.RawData, 0, 4, UnquoteString(value));
                break;
            case "@flags":
                var flagsVal = ParseUInt(value);
                BigEndian.WriteUInt32(rpj.Header.RawData, 0x08, flagsVal);
                break;
            case "@scene":
                ShiftJisHelper.WriteFixed(rpj.Header.RawData, 0x0C, 64, UnquoteString(value));
                break;
            case "@filename":
                ShiftJisHelper.WriteFixed(rpj.Header.RawData, 0x4C, 28, UnquoteString(value));
                break;
            case "@area":
                rpj.Sections.AreaType = ParseAreaType(value);
                break;
            case "@message":
                ShiftJisHelper.WriteFixed(rpj.Header.RawData, 0x170, 256, UnquoteString(value));
                break;
            case "@build_path":
                if (!rpj.Header.BuildPathsCleared)
                {
                    Array.Clear(rpj.Header.RawData, 0x68, 264);
                    rpj.Header.BuildPathWritePos = 0x68;
                    rpj.Header.BuildPathsCleared = true;
                }
                var bpBytes = ShiftJisHelper.Encode(UnquoteString(value));
                int bpPos = rpj.Header.BuildPathWritePos;
                if (bpPos + bpBytes.Length < 0x170)
                {
                    Array.Copy(bpBytes, 0, rpj.Header.RawData, bpPos, bpBytes.Length);
                    rpj.Header.BuildPathWritePos = bpPos + bpBytes.Length + 1;
                }
                break;
            case "@total_cmds": rpj.Sections.TotalScriptCmds = ParseUInt(value); break;
            case "@entry_count": rpj.Sections.EntryCount = ParseUInt(value); break;
            case "@file_size": rpj.OriginalFileSize = (int)ParseUInt(value); break;
            case "@section1": rpj.Sections.Section1Offset = ParseUInt(value); break;
            case "@section2": rpj.Sections.Section2Offset = ParseUInt(value); break;
            case "@section3": rpj.Sections.Section3Offset = ParseUInt(value); break;
            case "@section4": rpj.Sections.Section4Offset = ParseUInt(value); break;
            case "@data_base": rpj.Sections.DataBaseOffset = ParseUInt(value); break;
            case "@reserved0": rpj.Sections.Reserved0 = ParseUInt(value); break;
            case "@reserved1": rpj.Sections.Reserved1 = ParseUIntArray(value, 4); break;
            case "@reserved2": rpj.Sections.Reserved2 = ParseUIntArray(value, 4); break;
            case "@reserved3": rpj.Sections.Reserved3 = ParseUInt(value); break;
            case "@raw": return ParseRawGap(rpj, lines, i);
        }

        return i + 1;
    }

    private static int ParseRawGap(SceneFile rpj, string[] lines, int startLine)
    {
        var line = StripComment(lines[startLine].TrimEnd('\r')).Trim();
        var parts = line.Split(' ', 3);
        int offset = (int)ParseUInt(parts[1]);
        var hexBuilder = new StringBuilder(parts[2]);

        int i = startLine + 1;
        while (i < lines.Length)
        {
            var nextLine = StripComment(lines[i].TrimEnd('\r')).Trim();
            if (nextLine.StartsWith("@raw+ "))
            {
                hexBuilder.Append(nextLine[6..]);
                i++;
            }
            else break;
        }

        rpj.Gaps.Add(new RawGap
        {
            FileOffset = offset,
            Data = Convert.FromHexString(hexBuilder.ToString()),
        });

        return i;
    }

    private static int ParseEntry(SceneFile rpj, string[] lines, int i)
    {
        var line = StripComment(lines[i].TrimEnd('\r')).Trim();
        var entry = new Entry();
        var tokens = TokenizeLine(line);

        foreach (var tok in tokens)
        {
            if (tok.StartsWith("id="))
                entry.WriteBE(0x00, ParseUInt(tok[3..]));
            else if (tok.StartsWith("type="))
                entry.WriteBE(0x24, ParseEntryType(tok[5..]));
            else if (tok.StartsWith("offset="))
                entry.FileOffset = (int)ParseUInt(tok[7..]);
        }

        i++;
        while (i < lines.Length)
        {
            var bodyLine = StripComment(lines[i].TrimEnd('\r')).Trim();
            if (bodyLine == "}" || bodyLine == "};") { i++; break; }
            if (bodyLine.StartsWith("script "))
            {
                int scriptFileOffset = -1;
                int scriptFileSize = -1;
                var scriptTokens = TokenizeLine(bodyLine);
                foreach (var tok in scriptTokens)
                {
                    if (tok.StartsWith("file_offset=")) scriptFileOffset = (int)ParseUInt(tok[12..]);
                    else if (tok.StartsWith("size=")) scriptFileSize = (int)ParseUInt(tok[5..]);
                }
                i = ParseScriptBlock(rpj, entry, lines, i + 1, scriptFileOffset, scriptFileSize);
                continue;
            }
            if (bodyLine.Contains('='))
            {
                var eqParts = bodyLine.Split('=', 2);
                ApplyEntryField(entry, eqParts[0].Trim(), eqParts[1].Trim());
            }
            i++;
        }

        rpj.Entries.Add(entry);
        return i;
    }

    private static void ApplyEntryField(Entry entry, string field, string value)
    {
        switch (field)
        {
            case "name_raw":
                var nameBytes = Convert.FromHexString(value);
                Array.Copy(nameBytes, 0, entry.RawData, 0x04, Math.Min(nameBytes.Length, 20));
                break;
            case "reserved":
                var resBytes = Convert.FromHexString(value);
                Array.Copy(resBytes, 0, entry.RawData, 0x18, Math.Min(resBytes.Length, 0x08));
                break;
            case "ref_id": entry.WriteBE(0x20, ParseUInt(value)); break;
            case "position":
                var pos = ParseFloatTuple(value);
                entry.WriteBEFloat(0x28, pos[0]);
                entry.WriteBEFloat(0x2C, pos.Length > 1 ? pos[1] : 0f);
                entry.WriteBEFloat(0x30, pos.Length > 2 ? pos[2] : 0f);
                break;
            case "radius": entry.WriteBEFloat(0x34, ParseFloat(value)); break;
            case "target_refs":
                var refs = ParseUIntArray(value, 3);
                entry.WriteBE(0x38, refs[0]);
                entry.WriteBE(0x3C, refs[1]);
                entry.WriteBE(0x40, refs[2]);
                break;
            case "padding":
                var padBytes = Convert.FromHexString(value);
                Array.Copy(padBytes, 0, entry.RawData, 0x44, Math.Min(padBytes.Length, 0x14));
                break;
            case "overlap":
                var ovBytes = Convert.FromHexString(value);
                Array.Copy(ovBytes, 0, entry.RawData, 0x58, Math.Min(ovBytes.Length, 0x0C));
                break;
            case "script_block_count": entry.WriteBE(0x64, ParseUInt(value)); break;
            case "script_block_offset": entry.WriteBE(0x68, ParseUInt(value)); break;
            case "next_entry": entry.WriteBE(0x6C, ParseUInt(value)); break;
        }
    }

    private static int ParseScriptBlock(SceneFile rpj, Entry entry, string[] lines, int i,
        int explicitFileOffset = -1, int explicitFileSize = -1)
    {
        var block = new ScriptBlock();

        while (i < lines.Length)
        {
            var line = StripComment(lines[i].TrimEnd('\r')).Trim();

            if (line == "}" || line == "};") { i++; break; }
            if (string.IsNullOrEmpty(line)) { i++; continue; }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { i++; continue; }

            switch (parts[0])
            {
                case "header":
                    // Legacy format: header HEXBYTES (512 hex chars = 0x100 bytes)
                    block.HeaderData = Convert.FromHexString(parts.Length > 1 ? parts[1] : "");
                    break;
                case "sentinel":
                    // New format: decoded header fields
                    BigEndian.WriteUInt32(block.HeaderData, 0x00, ParseUInt(parts.Length > 2 ? parts[2] : parts[1]));
                    break;
                case "bytecode_size":
                    BigEndian.WriteUInt32(block.HeaderData, 0xEC, ParseUInt(parts.Length > 2 ? parts[2] : parts[1]));
                    break;
                case "param_size":
                    BigEndian.WriteUInt32(block.HeaderData, 0xF0, ParseUInt(parts.Length > 2 ? parts[2] : parts[1]));
                    break;
                case "next_block":
                    BigEndian.WriteUInt32(block.HeaderData, 0xFC, ParseUInt(parts.Length > 2 ? parts[2] : parts[1]));
                    break;
                case "header_middle":
                    var middleBytes = Convert.FromHexString(parts.Length > 2 ? parts[2] : parts[1]);
                    Array.Copy(middleBytes, 0, block.HeaderData, 0x04, Math.Min(middleBytes.Length, 0xE8));
                    break;
                case "header_tail":
                    var tailBytes = Convert.FromHexString(parts.Length > 2 ? parts[2] : parts[1]);
                    Array.Copy(tailBytes, 0, block.HeaderData, 0xF4, Math.Min(tailBytes.Length, 0x08));
                    break;
                case "param_data":
                    block.ParamData = Convert.FromHexString(parts.Length > 1 ? parts[1] : "");
                    break;
                case "data":
                    block.Elements.Add(new ScriptData
                    {
                        Bytes = Convert.FromHexString(parts.Length > 1 ? parts[1] : ""),
                    });
                    break;
                default:
                    // Instruction: opcode_name param1 param2 ...
                    if (!Opcodes.TryGetOpcode(parts[0], out uint opcode))
                        throw new FormatException($"Unknown opcode '{parts[0]}' at line {i + 1}");

                    var parms = new uint[parts.Length - 1];
                    for (int p = 1; p < parts.Length; p++)
                        parms[p - 1] = ParseUInt(parts[p]);

                    block.Elements.Add(new ScriptInstruction
                    {
                        Opcode = opcode,
                        Size = (uint)(8 + parms.Length * 4),
                        RawParams = parms,
                    });
                    break;
            }

            i++;
        }

        block.FileOffset = explicitFileOffset >= 0 ? explicitFileOffset : 0;
        block.FileSize = explicitFileSize >= 0 ? explicitFileSize : 0;
        block.OwnerEntryIndex = rpj.Entries.Count;
        entry.ScriptBlocks.Add(block);
        rpj.Scripts.Add(block);

        return i;
    }

    private static int ParseWaypoint(SceneFile rpj, string[] lines, int i)
    {
        var line = StripComment(lines[i].TrimEnd('\r')).Trim();
        var wp = new Waypoint();

        var tokens = TokenizeLine(line);
        foreach (var tok in tokens)
        {
            if (tok.StartsWith("offset="))
                wp.FileOffset = (int)ParseUInt(tok[7..]);
        }

        i++;
        while (i < lines.Length)
        {
            var bodyLine = StripComment(lines[i].TrimEnd('\r')).Trim();
            if (bodyLine == "}" || bodyLine == "};") { i++; break; }
            if (bodyLine.StartsWith("raw = "))
                wp.RawData = Convert.FromHexString(bodyLine[6..].Trim());
            i++;
        }

        rpj.Waypoints.Add(wp);
        return i;
    }

    // --- Helpers ---

    private static string StripComment(string line)
    {
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuote = !inQuote;
            if (line[i] == '#' && !inQuote) return line[..i];
        }
        return line;
    }

    private static string[] SplitDirective(string line)
    {
        int space = line.IndexOf(' ');
        if (space < 0) return [line];
        return [line[..space], line[(space + 1)..].Trim()];
    }

    private static List<string> TokenizeLine(string line)
    {
        var tokens = new List<string>();
        bool inQuote = false;
        var current = new StringBuilder();

        foreach (char c in line)
        {
            if (c == '"') { inQuote = !inQuote; current.Append(c); }
            else if (c == ' ' && !inQuote)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
            }
            else if (c == '{' || c == '}')
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static string UnquoteString(string s)
    {
        if (s.StartsWith('"') && s.EndsWith('"')) s = s[1..^1];
        return s.Replace("\\\\", "\x01").Replace("\\\"", "\"").Replace("\x01", "\\");
    }

    private static uint ParseUInt(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return uint.Parse(s, CultureInfo.InvariantCulture);
    }

    private static uint[] ParseUIntArray(string s, int expected)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new uint[expected];
        for (int i = 0; i < Math.Min(parts.Length, expected); i++)
            result[i] = ParseUInt(parts[i]);
        return result;
    }

    private static float[] ParseFloatTuple(string s)
    {
        s = s.Trim('(', ')', ' ');
        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(p => ParseFloat(p.Trim())).ToArray();
    }

    private static float ParseFloat(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Convert.FromHexString(s[2..]);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }
        return float.Parse(s, CultureInfo.InvariantCulture);
    }

    private static AreaType ParseAreaType(string s) => s.Trim() switch
    {
        "town" => AreaType.Town,
        "indoor" => AreaType.Indoor,
        "dungeon" => AreaType.Dungeon,
        "world" => AreaType.World,
        "cube" => AreaType.Cube,
        _ => (AreaType)ParseUInt(s),
    };

    private static uint ParseEntryType(string s) => s.Trim() switch
    {
        "spawn" => 0, "npc" => 1, "zone" => 2, "enemy" => 3,
        "link" => 4, "entity" => 5, "event" => 6,
        _ => ParseUInt(s),
    };

    private static void WriteHeaderASCII(byte[] header, int offset, int length, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        Array.Clear(header, offset, length);
        Array.Copy(bytes, 0, header, offset, Math.Min(bytes.Length, length));
    }

}
