using System.Globalization;
using System.Text;
using ShadowForge.Formats.RPJ;
using ShadowForge.IO;
using ShadowForge.Text;

namespace ShadowForge.Formats.BDSL;

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

            if (trimmed.StartsWith("scene "))
                i = ParseSceneBlock(rpj, lines, i);
            else if (IsEntryKeyword(trimmed))
                i = ParseEntryBlock(rpj, lines, i);
            else if (trimmed.StartsWith("waypoint "))
                i = ParseWaypointBlock(rpj, lines, i);
            else
                i++;
        }

        ComputeLayout(rpj);
        return rpj;
    }

    public static SceneFile ParseFile(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return Parse(text);
    }

    // --- Top-level block parsers ---

    private static int ParseSceneBlock(SceneFile rpj, string[] lines, int i)
    {
        // scene "NAME" {
        var line = StripComment(lines[i].TrimEnd('\r')).Trim();
        var tokens = TokenizeLine(line);

        // Extract scene name: second token
        if (tokens.Count >= 2)
        {
            string sceneName = UnquoteString(tokens[1]);
            ShiftJisHelper.WriteFixed(rpj.Header.RawData, 0x0C, 64, sceneName);
        }

        i++;
        while (i < lines.Length)
        {
            var bodyLine = StripComment(lines[i].TrimEnd('\r')).Trim();
            if (bodyLine == "}" || bodyLine == "};") { i++; break; }
            if (string.IsNullOrEmpty(bodyLine)) { i++; continue; }

            if (bodyLine.Contains('='))
            {
                var eqParts = bodyLine.Split('=', 2);
                ApplySceneField(rpj, eqParts[0].Trim(), eqParts[1].Trim());
            }
            i++;
        }

        return i;
    }

    private static void ApplySceneField(SceneFile rpj, string key, string value)
    {
        switch (key)
        {
            case "version":
                WriteHeaderASCII(rpj.Header.RawData, 0, 4, UnquoteString(value));
                break;
            case "flags":
                BigEndian.WriteUInt32(rpj.Header.RawData, 0x08, ParseUInt(value));
                break;
            case "area":
                rpj.Sections.AreaType = ParseAreaType(value);
                break;
            case "messages":
                ShiftJisHelper.WriteFixed(rpj.Header.RawData, 0x170, 256, UnquoteString(value));
                break;
            case "build_path":
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
        }
    }

    private static bool IsEntryKeyword(string trimmed)
    {
        return trimmed.StartsWith("spawn ") || trimmed.StartsWith("npc ") ||
               trimmed.StartsWith("zone ") || trimmed.StartsWith("enemy ") ||
               trimmed.StartsWith("link ") || trimmed.StartsWith("entity ") ||
               trimmed.StartsWith("warp ");
    }

    private static int ParseEntryBlock(SceneFile rpj, string[] lines, int i)
    {
        var line = StripComment(lines[i].TrimEnd('\r')).Trim();
        var tokens = TokenizeLine(line);
        var entry = new Entry();

        // First token is the type keyword
        string typeKeyword = tokens.Count > 0 ? tokens[0] : "";
        uint entryType = ParseEntryType(typeKeyword);
        entry.WriteBE(0x24, entryType);

        // Second token is the name (quoted)
        if (tokens.Count >= 2)
        {
            string entryName = UnquoteString(tokens[1]);
            ShiftJisHelper.WriteFixed(entry.RawData, 0x04, 20, entryName);
        }

        // Remaining tokens are key=value
        for (int t = 2; t < tokens.Count; t++)
        {
            var tok = tokens[t];
            if (tok.StartsWith("id="))
                entry.WriteBE(0x00, ParseUInt(tok[3..]));
        }

        // Check for comment-embedded offset
        var rawLine = lines[i].TrimEnd('\r');
        int commentIdx = FindCommentHash(rawLine);
        if (commentIdx >= 0)
        {
            var comment = rawLine[(commentIdx + 1)..].Trim();
            if (comment.StartsWith("offset="))
                entry.FileOffset = (int)ParseUInt(comment[7..]);
        }

        i++;
        while (i < lines.Length)
        {
            var bodyLine = StripComment(lines[i].TrimEnd('\r')).Trim();
            if (bodyLine == "}" || bodyLine == "};") { i++; break; }
            if (string.IsNullOrEmpty(bodyLine)) { i++; continue; }

            if (bodyLine.StartsWith("when "))
            {
                // Parse "when CONDITION_LIST {" with block body
                i = ParseWhenBlock(rpj, entry, lines, i);
                continue;
            }

            if (bodyLine.StartsWith("@"))
            {
                ApplyEntryRawDirective(entry, bodyLine);
                i++;
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
            case "position":
                var pos = ParseFloatTuple(value);
                entry.WriteBEFloat(0x28, pos.Length > 0 ? pos[0] : 0f);
                entry.WriteBEFloat(0x2C, pos.Length > 1 ? pos[1] : 0f);
                entry.WriteBEFloat(0x30, pos.Length > 2 ? pos[2] : 0f);
                break;
            case "facing":
                entry.WriteBEFloat(0x34, ParseFloat(value));
                break;
            case "ref_id":
                entry.WriteBE(0x20, ParseUInt(value));
                break;
            case "extents":
                var ext = ParseFloatTuple(value);
                entry.WriteBEFloat(0x38, ext.Length > 0 ? ext[0] : 0f);
                entry.WriteBEFloat(0x3C, ext.Length > 1 ? ext[1] : 0f);
                entry.WriteBEFloat(0x40, ext.Length > 2 ? ext[2] : 0f);
                break;
            case "yaw":
                entry.WriteBEFloat(0x44, ParseFloat(value));
                break;
            case "route":
                var route = ParseFloatTuple(value);
                entry.WriteBEFloat(0x38, route.Length > 0 ? route[0] : 0f);
                entry.WriteBEFloat(0x3C, route.Length > 1 ? route[1] : 0f);
                entry.WriteBEFloat(0x40, route.Length > 2 ? route[2] : 0f);
                break;
            case "aggro_radius":
                entry.WriteBEFloat(0x44, ParseFloat(value));
                break;
            case "rotation":
                entry.WriteBEFloat(0x44, ParseFloat(value));
                break;
        }
    }

    private static void ApplyEntryRawDirective(Entry entry, string line)
    {
        // @directive HEX or @directive value
        int spaceIdx = line.IndexOf(' ');
        string directive = spaceIdx >= 0 ? line[..spaceIdx] : line;
        string val = spaceIdx >= 0 ? line[(spaceIdx + 1)..].Trim() : "";

        switch (directive)
        {
            case "@name_bytes":
                var nameBytes = Convert.FromHexString(val);
                Array.Copy(nameBytes, 0, entry.RawData, 0x04, Math.Min(nameBytes.Length, 20));
                break;
            case "@padding":
                var padBytes = Convert.FromHexString(val);
                Array.Copy(padBytes, 0, entry.RawData, 0x48, Math.Min(padBytes.Length, 20));
                break;
            case "@overlap":
                var ovBytes = Convert.FromHexString(val);
                Array.Copy(ovBytes, 0, entry.RawData, 0x58, Math.Min(ovBytes.Length, 12));
                break;
            case "@reserved":
                var resBytes = Convert.FromHexString(val);
                Array.Copy(resBytes, 0, entry.RawData, 0x18, Math.Min(resBytes.Length, 8));
                break;
            case "@target_refs":
                var refParts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (refParts.Length > 0) entry.WriteBE(0x38, ParseUInt(refParts[0]));
                if (refParts.Length > 1) entry.WriteBE(0x3C, ParseUInt(refParts[1]));
                if (refParts.Length > 2) entry.WriteBE(0x40, ParseUInt(refParts[2]));
                break;
            case "@field_44":
                entry.WriteBEFloat(0x44, ParseFloat(val));
                break;
        }
    }

    private static int ParseWhenBlock(SceneFile rpj, Entry entry, string[] lines, int i)
    {
        // "when CONDITION_LIST {"  with possible trailing comment
        var rawLine = lines[i].TrimEnd('\r');
        var line = StripComment(rawLine).Trim();

        // Extract file_offset and size from comment if present
        int scriptFileOffset = -1;
        int scriptFileSize = -1;
        int commentIdx = FindCommentHash(rawLine);
        if (commentIdx >= 0)
        {
            var comment = rawLine[(commentIdx + 1)..].Trim();
            // "file_offset=0x2458, size=0x1C0"
            foreach (var cpart in comment.Split(',', StringSplitOptions.TrimEntries))
            {
                if (cpart.StartsWith("file_offset="))
                    scriptFileOffset = (int)ParseUInt(cpart[12..]);
                else if (cpart.StartsWith("size="))
                    scriptFileSize = (int)ParseUInt(cpart[5..]);
            }
        }

        var block = new ScriptBlock();
        int conditionIndex = 0;

        // Parse condition list from "when STUFF {"
        // Strip "when " prefix and trailing "{"
        string condPart = line;
        if (condPart.StartsWith("when ")) condPart = condPart[5..].Trim();
        int braceIdx = condPart.LastIndexOf('{');
        if (braceIdx >= 0) condPart = condPart[..braceIdx].Trim();

        ParseWhenConditions(block, condPart, ref conditionIndex);

        // Now parse body
        i++;
        while (i < lines.Length)
        {
            var bodyLine = StripComment(lines[i].TrimEnd('\r')).Trim();
            if (bodyLine == "}" || bodyLine == "};") { i++; break; }
            if (string.IsNullOrEmpty(bodyLine)) { i++; continue; }

            if (bodyLine.StartsWith("@"))
            {
                ApplyWhenDirective(block, bodyLine);
                i++;
                continue;
            }

            i = ParseInstruction(block, lines, i);
        }

        block.FileOffset = scriptFileOffset >= 0 ? scriptFileOffset : 0;
        block.FileSize = scriptFileSize >= 0 ? scriptFileSize : 0;
        block.OwnerEntryIndex = rpj.Entries.Count;
        entry.ScriptBlocks.Add(block);
        rpj.Scripts.Add(block);

        return i;
    }

    private static void ParseWhenConditions(ScriptBlock block, string condPart, ref int conditionIndex)
    {
        // condPart could be:
        //   "all"
        //   "chapter 0..59"
        //   "chapter 0..59, variable[6] == 0"
        //   "variable[6] == 0"
        //   etc.

        if (condPart == "all")
        {
            BigEndian.WriteUInt32(block.HeaderData, 0x00, 0xFFFFFFFF);
            return;
        }

        // Split by comma, handling each part
        var parts = condPart.Split(',', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("chapter "))
            {
                var chapterStr = part[8..].Trim();
                if (chapterStr.Contains(".."))
                {
                    var range = chapterStr.Split("..");
                    BigEndian.WriteUInt32(block.HeaderData, 0x00, ParseUInt(range[0]));
                    BigEndian.WriteUInt32(block.HeaderData, 0x04, ParseUInt(range[1]));
                }
                else
                {
                    var v = ParseUInt(chapterStr);
                    BigEndian.WriteUInt32(block.HeaderData, 0x00, v);
                    BigEndian.WriteUInt32(block.HeaderData, 0x04, v);
                }
            }
            else
            {
                // TYPE[OPERAND] OP VALUE or TYPE OPERAND OP VALUE
                ParseConditionExpression(block, conditionIndex++, part);
            }
        }
    }

    private static void ParseConditionExpression(ScriptBlock block, int index, string expr)
    {
        // Handles: variable[6] == 0   or   flag 0x0A >= 1   or   variable 6 == 0
        if (index >= ScriptHeader.ConditionCount) return;
        int offset = ScriptHeader.ConditionsOffset + index * ScriptHeader.ConditionSize;

        expr = expr.Trim();
        if (string.IsNullOrEmpty(expr)) return;

        uint type = 0;
        uint operand = 0;
        uint op = 0;
        uint value = 0;

        // Check for TYPE[OPERAND] syntax
        int bracketOpen = expr.IndexOf('[');
        int bracketClose = expr.IndexOf(']');
        if (bracketOpen >= 0 && bracketClose > bracketOpen)
        {
            string typeName = expr[..bracketOpen].Trim();
            type = ParseCondType(typeName);
            operand = ParseUInt(expr[(bracketOpen + 1)..bracketClose]);
            // Remaining after "]": " OP VALUE"
            string rest = expr[(bracketClose + 1)..].Trim();
            var restParts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (restParts.Length >= 2)
            {
                op = ParseCondOp(restParts[0]);
                value = ParseUInt(restParts[1]);
            }
        }
        else
        {
            // Space-separated: TYPE OPERAND OP VALUE
            var parts = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return;
            type = ParseCondType(parts[0]);
            operand = ParseUInt(parts[1]);
            op = ParseCondOp(parts[2]);
            value = ParseUInt(parts[3]);
        }

        BigEndian.WriteUInt32(block.HeaderData, offset, type);
        BigEndian.WriteUInt32(block.HeaderData, offset + 4, operand);
        BigEndian.WriteUInt32(block.HeaderData, offset + 8, op);
        BigEndian.WriteUInt32(block.HeaderData, offset + 12, value);
    }

    private static uint ParseCondType(string s) => s.Trim() switch
    {
        "variable" => 2,
        "item_count" => 3,
        "party_check" => 4,
        "flag" => 5,
        _ when s.StartsWith("unk_") => ParseUInt(s[4..]),
        _ => ParseUInt(s),
    };

    private static uint ParseCondOp(string s) => s.Trim() switch
    {
        "==" => 0,
        ">=" => 1,
        "<=" => 2,
        ">" => 3,
        "<" => 4,
        "!=" => 5,
        _ when s.StartsWith("op_") => ParseUInt(s[3..]),
        _ => ParseUInt(s),
    };

    private static void ApplyWhenDirective(ScriptBlock block, string line)
    {
        int spaceIdx = line.IndexOf(' ');
        string directive = spaceIdx >= 0 ? line[..spaceIdx] : line;
        string val = spaceIdx >= 0 ? line[(spaceIdx + 1)..].Trim() : "";

        switch (directive)
        {
            case "@block_type":
                uint blockType = val.Trim() switch
                {
                    "event" => 1,
                    "npc" => 2,
                    "auto_run" => 3,
                    "link" => 4,
                    "cube" => 5,
                    _ => ParseUInt(val),
                };
                BigEndian.WriteUInt32(block.HeaderData, 0xE0, blockType);
                break;

            case "@render":
                uint renderFlags = 0;
                foreach (var flag in val.Split(',', StringSplitOptions.TrimEntries))
                {
                    renderFlags |= flag switch
                    {
                        "visible" => 0x1u,
                        "type_b" => 0x2u,
                        "variant_b" => 0x10u,
                        "variant_a" => 0x20u,
                        "special_anim" => 0x08000000u,
                        "collision" => 0x10000000u,
                        "has_model" => 0x80000000u,
                        _ => 0u,
                    };
                }
                BigEndian.WriteUInt32(block.HeaderData, 0xD4, renderFlags);
                break;

            case "@behavior":
                uint behaviorMode = val.Trim() switch
                {
                    "default" => 1,
                    "alternate" => 2,
                    "disabled" => 0xFFFFFFFF,
                    _ => ParseUInt(val),
                };
                BigEndian.WriteUInt32(block.HeaderData, 0xD8, behaviorMode);
                break;

            case "@auto_run":
                BigEndian.WriteUInt32(block.HeaderData, 0x90, ParseUInt(val));
                break;

            case "@encounter":
                uint encounterMode = val.Trim() switch
                {
                    "normal" => 1,
                    "boss" => 2,
                    _ => ParseUInt(val),
                };
                BigEndian.WriteUInt32(block.HeaderData, 0x94, encounterMode);
                break;

            case "@encounter_range":
                BigEndian.WriteFloat(block.HeaderData, 0x98, ParseFloat(val));
                break;

            case "@auto_set_var":
                BigEndian.WriteUInt32(block.HeaderData, 0x9C, 1);
                BigEndian.WriteUInt32(block.HeaderData, 0xA0, ParseUInt(val));
                break;

            case "@linked_entry":
                BigEndian.WriteUInt32(block.HeaderData, 0xC4, ParseUInt(val));
                break;

            case "@spawn_config":
                BigEndian.WriteUInt32(block.HeaderData, 0xC8, ParseUInt(val));
                break;

            case "@spawn_angle":
                BigEndian.WriteFloat(block.HeaderData, 0xCC, ParseFloat(val));
                break;

            case "@spawn_scale":
                BigEndian.WriteFloat(block.HeaderData, 0xD0, ParseFloat(val));
                break;

            case "@interaction_radius":
                BigEndian.WriteFloat(block.HeaderData, 0xDC, ParseFloat(val));
                break;

            case "@type_data":
                BigEndian.WriteUInt32(block.HeaderData, 0xE4, ParseUInt(val));
                break;

            case "@spawn_mode":
                BigEndian.WriteUInt32(block.HeaderData, 0xE8, ParseUInt(val));
                break;

            case "@raw_header":
                // @raw_header 0xOFFSET 0xVALUE
                var rawParts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (rawParts.Length >= 2)
                {
                    int rawOffset = (int)ParseUInt(rawParts[0]);
                    uint rawValue = ParseUInt(rawParts[1]);
                    if (rawOffset + 4 <= block.HeaderData.Length)
                        BigEndian.WriteUInt32(block.HeaderData, rawOffset, rawValue);
                }
                break;
        }
    }

    private static int ParseInstruction(ScriptBlock block, string[] lines, int i)
    {
        var rawLine = lines[i].TrimEnd('\r');

        // Extract opcode hint from comment: # op=NNNN
        uint? aliasOpcode = null;
        int commentIdx = FindCommentHash(rawLine);
        if (commentIdx >= 0)
        {
            var comment = rawLine[(commentIdx + 1)..].Trim();
            if (comment.StartsWith("op="))
            {
                if (uint.TryParse(comment[3..].Trim(), out uint aliasVal))
                    aliasOpcode = aliasVal;
            }
        }

        var bodyLine = StripComment(rawLine).Trim();
        if (string.IsNullOrEmpty(bodyLine)) return i + 1;

        // Handle function-call syntax: name(args) or legacy space-separated: name args
        string name;
        string argsPart;
        int parenIdx = bodyLine.IndexOf('(');
        if (parenIdx > 0 && bodyLine.EndsWith(")"))
        {
            name = bodyLine[..parenIdx].Trim();
            argsPart = bodyLine[(parenIdx + 1)..^1]; // strip parens
        }
        else
        {
            var parts = bodyLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            name = parts[0];
            argsPart = parts.Length > 1 ? parts[1] : "";
        }

        if (name == "raw")
        {
            block.Elements.Add(new ScriptData
            {
                Bytes = !string.IsNullOrEmpty(argsPart) ? Convert.FromHexString(argsPart.Trim()) : Array.Empty<byte>(),
            });
            return i + 1;
        }

        if (name == "param_data")
        {
            block.ParamData = !string.IsNullOrEmpty(argsPart) ? Convert.FromHexString(argsPart.Trim()) : Array.Empty<byte>();
            return i + 1;
        }

        // Parse as instruction
        if (!Opcodes.TryGetOpcode(name, out uint opcode))
            throw new FormatException($"Unknown opcode '{name}' at line {i + 1}");

        if (aliasOpcode.HasValue)
            opcode = aliasOpcode.Value;

        // Parse params: comma-separated, strip name= prefixes
        var paramTokens = new List<string>();
        if (!string.IsNullOrEmpty(argsPart))
        {
            foreach (var tok in argsPart.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = tok.Trim();
                // Strip "name=value" -> "value"
                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx >= 0)
                    trimmed = trimmed[(eqIdx + 1)..].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    paramTokens.Add(trimmed);
            }
        }

        var rawParams = new uint[paramTokens.Count];
        for (int p = 0; p < paramTokens.Count; p++)
            rawParams[p] = ParseUInt(paramTokens[p]);

        block.Elements.Add(new ScriptInstruction
        {
            Opcode = opcode,
            Size = (uint)(8 + rawParams.Length * 4),
            RawParams = rawParams,
        });

        return i + 1;
    }

    private static int ParseWaypointBlock(SceneFile rpj, string[] lines, int i)
    {
        // waypoint ID [type=TYPE] at (X, Y, Z) [rotation=R] {
        var line = StripComment(lines[i].TrimEnd('\r')).Trim();
        var wp = new Waypoint();

        // Tokenize the header line, respecting parens and quotes
        // Simple approach: find "at (" to split
        string headerPart = line;
        // Remove trailing "{"
        int bracePos = headerPart.LastIndexOf('{');
        if (bracePos >= 0) headerPart = headerPart[..bracePos].Trim();

        // Parse "waypoint ID [type=TYPE] at (X, Y, Z) [rotation=R]"
        // Find "at (" token
        int atIdx = -1;
        var headerTokens = TokenizeLine(headerPart);
        for (int t = 0; t < headerTokens.Count; t++)
        {
            if (headerTokens[t] == "at") { atIdx = t; break; }
        }

        // Token 1 = ID
        if (headerTokens.Count >= 2)
            wp.WriteBE(0x00, ParseUInt(headerTokens[1]));

        // Scan tokens before "at" for type=
        int beforeAt = atIdx >= 0 ? atIdx : headerTokens.Count;
        for (int t = 2; t < beforeAt; t++)
        {
            var tok = headerTokens[t];
            if (tok.StartsWith("type="))
            {
                uint wpType = tok[5..] switch
                {
                    "waypoint" => 0,
                    "resource" => 5,
                    "spawn" => 6,
                    _ => ParseUInt(tok[5..]),
                };
                wp.WriteBE(0x04, wpType);
            }
        }

        // Position: collect tokens after "at" that form the tuple
        if (atIdx >= 0 && atIdx + 1 < headerTokens.Count)
        {
            // Reconstruct the tuple from tokens
            var tupleBuilder = new StringBuilder();
            bool inTuple = false;
            for (int t = atIdx + 1; t < headerTokens.Count; t++)
            {
                var tok = headerTokens[t];
                if (tok.StartsWith("(")) inTuple = true;
                if (inTuple)
                {
                    tupleBuilder.Append(tok);
                    tupleBuilder.Append(' ');
                    if (tok.EndsWith(")")) break;
                }
                else if (!inTuple && tok.StartsWith("rotation="))
                {
                    // Already parsed, handled below
                }
            }

            string tupleStr = tupleBuilder.ToString().Trim();
            if (!string.IsNullOrEmpty(tupleStr))
            {
                var pos = ParseFloatTuple(tupleStr);
                if (pos.Length > 0) wp.WriteBEFloat(0x10, pos[0]);
                if (pos.Length > 1) wp.WriteBEFloat(0x14, pos[1]);
                if (pos.Length > 2) wp.WriteBEFloat(0x18, pos[2]);
            }
        }

        // rotation= token after the tuple
        foreach (var tok in headerTokens)
        {
            if (tok.StartsWith("rotation="))
                wp.WriteBEFloat(0x1C, ParseFloat(tok[9..]));
        }

        i++;
        while (i < lines.Length)
        {
            var bodyLine = StripComment(lines[i].TrimEnd('\r')).Trim();
            if (bodyLine == "}" || bodyLine == "};") { i++; break; }
            if (string.IsNullOrEmpty(bodyLine)) { i++; continue; }

            if (bodyLine.StartsWith("condition "))
            {
                // condition TYPE OPERAND OP VALUE
                var condParts = bodyLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (condParts.Length >= 5)
                {
                    uint condType = ParseCondType(condParts[1]);
                    uint condOperand = ParseUInt(condParts[2]);
                    uint condOp = ParseCondOp(condParts[3]);
                    uint condValue = ParseUInt(condParts[4]);
                    // Pack type+op into flags u32: type in low nibble, op in next nibble
                    uint flags = (condType & 0x0F) | ((condOp & 0x0F) << 4);
                    wp.WriteBE(0x08, flags);
                    wp.WriteBE(0x0C, condValue);
                    // Also store operand? The layout says conditionFlags = type<<0 | op<<4
                    // operand not explicitly mapped - store in +0x08 upper bits if needed
                    // Per spec, pack type+op into u32 at +0x08, value at +0x0C
                    _ = condOperand; // operand noted but not in 0x08/0x0C layout
                }
            }
            else if (bodyLine.StartsWith("ref = "))
            {
                // ref = "name" -> byte-swap ASCII into +0x20 (16 bytes)
                string refName = UnquoteString(bodyLine[6..].Trim());
                var refBytes = Encoding.ASCII.GetBytes(refName);
                // Byte-swap: process as u32 big-endian words
                var dest = new byte[16];
                int copyLen = Math.Min(refBytes.Length, 16);
                Array.Copy(refBytes, 0, dest, 0, copyLen);
                // Byte-swap each u32
                for (int b = 0; b + 3 < 16; b += 4)
                {
                    (dest[b], dest[b + 3]) = (dest[b + 3], dest[b]);
                    (dest[b + 1], dest[b + 2]) = (dest[b + 2], dest[b + 1]);
                }
                Array.Copy(dest, 0, wp.RawData, 0x20, 16);
            }
            else if (bodyLine.StartsWith("priority = ") || bodyLine.StartsWith("priority="))
            {
                string priVal = bodyLine.Contains('=') ? bodyLine.Split('=', 2)[1].Trim() : "";
                wp.WriteBE(0x20, ParseUInt(priVal));
            }
            else if (bodyLine.StartsWith("@ref_data "))
            {
                var refHex = bodyLine[10..].Trim();
                var refBytes = Convert.FromHexString(refHex);
                Array.Copy(refBytes, 0, wp.RawData, 0x20, Math.Min(refBytes.Length, 16));
            }
            else if (bodyLine.StartsWith("targets = [") || bodyLine.StartsWith("targets=["))
            {
                // targets = [T0, T1, T2]
                int lbrace = bodyLine.IndexOf('[');
                int rbrace = bodyLine.IndexOf(']');
                if (lbrace >= 0 && rbrace > lbrace)
                {
                    string inner = bodyLine[(lbrace + 1)..rbrace];
                    var tParts = inner.Split(',', StringSplitOptions.TrimEntries);
                    if (tParts.Length > 0) wp.WriteBE(0x30, ParseUInt(tParts[0]));
                    if (tParts.Length > 1) wp.WriteBE(0x34, ParseUInt(tParts[1]));
                    if (tParts.Length > 2) wp.WriteBE(0x38, ParseUInt(tParts[2]));
                }
            }

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

    private static int FindCommentHash(string line)
    {
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuote = !inQuote;
            if (line[i] == '#' && !inQuote) return i;
        }
        return -1;
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
            else if ((c == '{' || c == '}') && !inQuote)
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
        s = s.Trim();
        if (s.StartsWith('"') && s.EndsWith('"')) s = s[1..^1];
        return s.Replace("\\\\", "\x01").Replace("\\\"", "\"").Replace("\x01", "\\");
    }

    private static uint ParseUInt(string s)
    {
        s = s.Trim().TrimEnd(',');
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return uint.Parse(s, CultureInfo.InvariantCulture);
    }

    private static float[] ParseFloatTuple(string s)
    {
        s = s.Trim().Trim('(', ')');
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

    private static void WriteHeaderASCII(byte[] header, int offset, int length, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        Array.Clear(header, offset, length);
        Array.Copy(bytes, 0, header, offset, Math.Min(bytes.Length, length));
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
        "spawn" => 0,
        "npc" => 1,
        "zone" => 2,
        "enemy" => 3,
        "link" => 4,
        "entity" => 5,
        "warp" => 6,
        _ => ParseUInt(s),
    };

    // -----------------------------------------------------------------
    // Layout computation - assigns file offsets, sizes, section table
    // -----------------------------------------------------------------

    private static void ComputeLayout(SceneFile rpj)
    {
        const int HeaderSize = 0x270;
        const int SectionTableSize = 0x48;
        const int DataBase = HeaderSize + SectionTableSize; // 0x2B8
        const int EntrySize = 0x70;

        var st = rpj.Sections;
        st.DataBaseOffset = DataBase;

        // Entry pool: contiguous entries starting at DataBase
        int entryPoolSize = rpj.Entries.Count * EntrySize;
        for (int e = 0; e < rpj.Entries.Count; e++)
        {
            var entry = rpj.Entries[e];
            entry.FileOffset = DataBase + e * EntrySize;

            // Chain linked list: next_entry is relative to DataBase
            uint nextOffset = (e < rpj.Entries.Count - 1) ? (uint)((e + 1) * EntrySize) : 0;
            entry.WriteBE(0x6C, nextOffset);
        }

        st.Section1Offset = (uint)entryPoolSize;
        st.EntryCount = (uint)rpj.Entries.Count;

        // Script section: starts after entry pool
        int scriptBase = DataBase + entryPoolSize;
        int scriptPos = 0; // relative to scriptBase
        uint totalCmds = 0;

        for (int e = 0; e < rpj.Entries.Count; e++)
        {
            var entry = rpj.Entries[e];
            var blocks = entry.ScriptBlocks;

            if (blocks.Count == 0)
            {
                entry.WriteBE(0x64, 0); // script_block_count
                entry.WriteBE(0x68, 0); // script_block_offset
                continue;
            }

            entry.WriteBE(0x64, (uint)blocks.Count);
            entry.WriteBE(0x68, (uint)scriptPos); // relative to scriptBase

            for (int b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                block.FileOffset = scriptBase + scriptPos;
                block.OwnerEntryIndex = e;

                // Compute bytecode size
                int bytecodeSize = 0;
                foreach (var elem in block.Elements)
                {
                    if (elem is ScriptInstruction instr)
                    {
                        bytecodeSize += (int)instr.Size;
                        totalCmds++;
                    }
                    else if (elem is ScriptData data)
                        bytecodeSize += data.Bytes.Length;
                }

                int paramSize = block.ParamData.Length;
                int blockTotalSize = 0x100 + bytecodeSize + paramSize;
                block.FileSize = blockTotalSize;

                // Write sizes into header data
                BigEndian.WriteUInt32(block.HeaderData, 0xEC, (uint)bytecodeSize);
                BigEndian.WriteUInt32(block.HeaderData, 0xF0, (uint)paramSize);

                // Chain: next_block is relative to scriptBase
                if (b < blocks.Count - 1)
                {
                    uint nextBlockRel = (uint)(scriptPos + blockTotalSize);
                    BigEndian.WriteUInt32(block.HeaderData, 0xFC, nextBlockRel);
                }
                else
                {
                    BigEndian.WriteUInt32(block.HeaderData, 0xFC, 0);
                }

                scriptPos += blockTotalSize;
            }
        }

        int scriptSectionSize = scriptPos;
        st.Section2Offset = (uint)scriptSectionSize;
        st.TotalScriptCmds = totalCmds;

        // Waypoint section: starts after scripts
        int waypointStart = scriptBase + scriptSectionSize;
        st.Section4Offset = (uint)waypointStart;

        for (int w = 0; w < rpj.Waypoints.Count; w++)
        {
            rpj.Waypoints[w].FileOffset = waypointStart + w * 0x40;
        }

        int totalSize = waypointStart + rpj.Waypoints.Count * 0x40;
        rpj.OriginalFileSize = totalSize;

        // Section3 (build tool metadata, not used at runtime)
        st.Section3Offset = 0;
    }
}
