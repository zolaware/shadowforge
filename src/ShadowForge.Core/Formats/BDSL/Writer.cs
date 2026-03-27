using System.Globalization;
using System.Text;
using ShadowForge.Formats.RPJ;
using ShadowForge.IO;
using ShadowForge.Text;

namespace ShadowForge.Formats.BDSL;

public static class Writer
{
    public static string Write(SceneFile rpj)
    {
        var sb = new StringBuilder();

        WriteSceneHeader(sb, rpj);

        foreach (var entry in rpj.Entries)
        {
            sb.AppendLine();
            WriteEntry(sb, entry);
        }

        foreach (var wp in rpj.Waypoints)
        {
            sb.AppendLine();
            WriteWaypoint(sb, wp);
        }

        return sb.ToString();
    }

    public static void Write(SceneFile rpj, string path)
    {
        var text = Write(rpj);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    // -------------------------------------------------------------------------
    // Scene header
    // -------------------------------------------------------------------------

    private static void WriteSceneHeader(StringBuilder sb, SceneFile rpj)
    {
        var h = rpj.Header;
        var st = rpj.Sections;

        sb.AppendLine($"scene \"{EscapeString(h.SceneName)}\" {{");
        sb.AppendLine($"    version = \"{h.Version}\"");

        if (h.Flags != 0)
            sb.AppendLine($"    flags = {h.Flags}");

        sb.AppendLine($"    area = {AreaTypeName(st.AreaType)}");

        if (!string.IsNullOrEmpty(h.MessagePath))
            sb.AppendLine($"    messages = \"{EscapeString(h.MessagePath)}\"");

        foreach (var bp in DecodeBuildPaths(h.RawData, 0x68, 264))
            sb.AppendLine($"    build_path = \"{EscapeString(bp)}\"");

        sb.AppendLine("}");
    }

    // -------------------------------------------------------------------------
    // Entry blocks
    // -------------------------------------------------------------------------

    private static void WriteEntry(StringBuilder sb, Entry entry)
    {
        string keyword = EntryTypeName(entry.EntryType);

        sb.Append($"{keyword} \"{EscapeString(entry.EntryName)}\" id={entry.EntryId} {{");

        if (entry.FileOffset != 0)
            sb.Append($"    # offset=0x{entry.FileOffset:X}");

        sb.AppendLine();

        // name_bytes: dropped - the parser writes the name and zero-pads the rest

        // +0x18..+0x1F - reserved region between name and ref_id (8 bytes)
        var reserved = entry.RawData[0x18..0x20];
        if (reserved.Any(b => b != 0))
            sb.AppendLine($"    @reserved {Convert.ToHexString(reserved)}");

        // position - omit if all-zero
        if (entry.EntryPosX != 0f || entry.EntryPosY != 0f || entry.EntryPosZ != 0f)
            sb.AppendLine($"    position = ({FormatFloat(entry.EntryPosX)}, {FormatFloat(entry.EntryPosY)}, {FormatFloat(entry.EntryPosZ)})");

        // facing (+0x34) - omit zero
        if (entry.EntryFacing != 0f)
            sb.AppendLine($"    facing = {FormatFloat(entry.EntryFacing)}");

        // Type-dependent +0x38-0x44 fields
        switch (entry.EntryType)
        {
            case 1: // npc - extents + yaw
                if (entry.ExtentX != 0f || entry.ExtentY != 0f || entry.ExtentZ != 0f)
                    sb.AppendLine($"    extents = ({FormatFloat(entry.ExtentX)}, {FormatFloat(entry.ExtentY)}, {FormatFloat(entry.ExtentZ)})");
                if (entry.EntryField44 != 0f)
                    sb.AppendLine($"    yaw = {FormatFloat(entry.EntryField44)}");
                break;

            case 3: // enemy - route + aggro_radius
                if (entry.ExtentX != 0f || entry.ExtentY != 0f || entry.ExtentZ != 0f)
                    sb.AppendLine($"    route = ({FormatFloat(entry.ExtentX)}, {FormatFloat(entry.ExtentY)}, {FormatFloat(entry.ExtentZ)})");
                if (entry.EntryField44 != 0f)
                    sb.AppendLine($"    aggro_radius = {FormatFloat(entry.EntryField44)}");
                break;

            case 6: // warp - rotation only from +0x44
                if (entry.EntryField44 != 0f)
                    sb.AppendLine($"    rotation = {FormatFloat(entry.EntryField44)}");
                break;

            default:
                // Emit non-zero target refs as @target_refs
                uint t0 = entry.EntryTargetRef0;
                uint t1 = entry.EntryTargetRef1;
                uint t2 = entry.EntryTargetRef2;
                if (t0 != 0 || t1 != 0 || t2 != 0)
                    sb.AppendLine($"    @target_refs 0x{t0:X} 0x{t1:X} 0x{t2:X}");
                break;
        }

        // +0x44..+0x57 - padding region (20 bytes after the type-dependent fields)
        var padding = entry.RawData[0x48..0x58];
        if (padding.Any(b => b != 0))
            sb.AppendLine($"    @padding {Convert.ToHexString(padding)}");

        // +0x58..+0x63 - overlap region (12 bytes)
        var overlap = entry.RawData[0x58..0x64];
        if (overlap.Any(b => b != 0))
            sb.AppendLine($"    @overlap {Convert.ToHexString(overlap)}");

        // Script blocks
        foreach (var script in entry.ScriptBlocks)
            WriteScriptBlock(sb, script);

        sb.AppendLine("}");
    }

    // -------------------------------------------------------------------------
    // Script blocks - when syntax
    // -------------------------------------------------------------------------

    private static void WriteScriptBlock(StringBuilder sb, ScriptBlock script)
    {
        var hdr = script.Header;

        // Skip empty blocks (no metadata, no instructions, no param data)
        bool hasMetadata = hdr.HasAnyMetadata || !hdr.UnknownRegionsAreZero;
        bool hasContent = script.Elements.Count > 0 || script.ParamData.Length > 0;
        if (!hasMetadata && !hasContent)
            return;

        sb.AppendLine();

        // Build the when line
        var whenLine = new StringBuilder("    when ");

        if (hdr.IsUnconditional)
        {
            whenLine.Append("all");
        }
        else
        {
            whenLine.Append($"chapter {hdr.ChapterMin}..{hdr.ChapterMax}");
        }

        // Append non-empty conditions
        for (int c = 0; c < ScriptHeader.ConditionCount; c++)
        {
            var cond = hdr.GetCondition(c);
            if (cond.IsEmpty) continue;
            whenLine.Append($", {cond.TypeName}[{cond.Operand}] {cond.OpName} {cond.Value}");
        }

        whenLine.Append(" {");

        // Trailing comment with file_offset and size
        if (script.FileOffset != 0 || script.FileSize != 0)
            whenLine.Append($"    # file_offset=0x{script.FileOffset:X}, size=0x{script.FileSize:X}");

        sb.AppendLine(whenLine.ToString());

        // Metadata (@-prefixed fields) - only emit non-zero values
        WriteBlockMetadata(sb, hdr);

        // Unknown regions fallback
        if (!hdr.UnknownRegionsAreZero)
        {
            // +0x88..+0x8F (8 bytes = 2 u32s)
            for (int off = 0x88; off < 0x90; off += 4)
            {
                uint val = BigEndian.ReadUInt32(hdr.RawBytes, off);
                if (val != 0)
                    sb.AppendLine($"        @raw_header 0x{off:X2} 0x{val:X8}");
            }
            // +0xA4..+0xC3 (32 bytes = 8 u32s)
            for (int off = 0xA4; off < 0xC4; off += 4)
            {
                uint val = BigEndian.ReadUInt32(hdr.RawBytes, off);
                if (val != 0)
                    sb.AppendLine($"        @raw_header 0x{off:X2} 0x{val:X8}");
            }
        }

        // Instructions and data elements
        foreach (var elem in script.Elements)
        {
            if (elem is ScriptInstruction instr)
                WriteInstruction(sb, instr);
            else if (elem is ScriptData rawData)
                sb.AppendLine($"        raw {Convert.ToHexString(rawData.Bytes)}");
        }

        // param_data
        if (script.ParamData.Length > 0)
            sb.AppendLine($"        param_data {Convert.ToHexString(script.ParamData)}");

        sb.AppendLine("    }");
    }

    private static void WriteBlockMetadata(StringBuilder sb, ScriptHeader hdr)
    {
        // @block_type: 1=event, 2=npc, 3=auto_run, 4=link, 5=cube
        if (hdr.BlockType != 0)
        {
            string btName = hdr.BlockType switch
            {
                1 => "event",
                2 => "npc",
                3 => "auto_run",
                4 => "link",
                5 => "cube",
                _ => $"0x{hdr.BlockType:X}",
            };
            sb.AppendLine($"        @block_type {btName}");
        }

        // @render: flag names for RenderFlags
        if (hdr.RenderFlags != 0)
        {
            var flags = DecodeRenderFlags(hdr.RenderFlags);
            sb.AppendLine($"        @render {string.Join(", ", flags)}");
        }

        // @behavior: 1=default, 2=alternate, -1=disabled
        if (hdr.BehaviorMode != 0)
        {
            string bName = hdr.BehaviorMode switch
            {
                1 => "default",
                2 => "alternate",
                -1 => "disabled",
                _ => hdr.BehaviorMode.ToString(),
            };
            sb.AppendLine($"        @behavior {bName}");
        }

        if (hdr.AutoRunFlags != 0)
            sb.AppendLine($"        @auto_run 0x{hdr.AutoRunFlags:X}");

        // @encounter: 1=normal, 2=boss
        if (hdr.EncounterMode != 0)
        {
            string encName = hdr.EncounterMode switch
            {
                1 => "normal",
                2 => "boss",
                _ => $"0x{hdr.EncounterMode:X}",
            };
            sb.AppendLine($"        @encounter {encName}");
        }

        if (hdr.EncounterRange != 0f)
            sb.AppendLine($"        @encounter_range {FormatFloat(hdr.EncounterRange)}");

        // @auto_set_var: emit index when flag is set
        if (hdr.AutoSetVarFlag != 0)
            sb.AppendLine($"        @auto_set_var {hdr.AutoSetVarIndex}");

        if (hdr.LinkedEntryId != 0)
            sb.AppendLine($"        @linked_entry {hdr.LinkedEntryId}");

        if (hdr.SpawnConfig != 0)
            sb.AppendLine($"        @spawn_config 0x{hdr.SpawnConfig:X}");

        if (hdr.SpawnAngle != 0f)
            sb.AppendLine($"        @spawn_angle {FormatFloat(hdr.SpawnAngle)}");

        if (hdr.SpawnScale != 0f)
            sb.AppendLine($"        @spawn_scale {FormatFloat(hdr.SpawnScale)}");

        if (hdr.InteractionRadius != 0f)
            sb.AppendLine($"        @interaction_radius {FormatFloat(hdr.InteractionRadius)}");

        if (hdr.TypeData != 0)
            sb.AppendLine($"        @type_data 0x{hdr.TypeData:X}");

        if (hdr.SpawnMode != 0)
            sb.AppendLine($"        @spawn_mode 0x{hdr.SpawnMode:X}");
    }

    private static List<string> DecodeRenderFlags(uint flags)
    {
        var names = new List<string>();
        if ((flags & (1u << 0)) != 0)  names.Add("visible");
        if ((flags & (1u << 1)) != 0)  names.Add("type_b");
        if ((flags & (1u << 4)) != 0)  names.Add("variant_b");
        if ((flags & (1u << 5)) != 0)  names.Add("variant_a");
        if ((flags & (1u << 27)) != 0) names.Add("special_anim");
        if ((flags & (1u << 28)) != 0) names.Add("collision");
        if ((flags & (1u << 31)) != 0) names.Add("has_model");

        // Emit remaining unknown bits as hex residual
        uint known = (1u << 0) | (1u << 1) | (1u << 4) | (1u << 5) | (1u << 27) | (1u << 28) | (1u << 31);
        uint unknown = flags & ~known;
        if (unknown != 0)
            names.Add($"0x{unknown:X}");

        return names;
    }

    // -------------------------------------------------------------------------
    // Instructions
    // -------------------------------------------------------------------------

    private static void WriteInstruction(StringBuilder sb, ScriptInstruction instr)
    {
        var rawParams = instr.RawParams;
        bool isAlias = Opcodes.IsAlias(instr.Opcode);
        string name = isAlias ? Opcodes.GetAliasName(instr.Opcode) : Opcodes.GetName(instr.Opcode);
        uint canonical = isAlias ? instr.Opcode : instr.Opcode; // for param lookup
        // Aliases share param names with their canonical opcode
        uint lookupOp = instr.Opcode;
        if (Opcodes.IsAlias(instr.Opcode))
        {
            // Map alias to canonical for param name lookup
            lookupOp = instr.Opcode switch
            {
                5067 => 5001, 5068 => 5025, 5070 => 5031,
                5096 => 5012, 5097 => 5003, _ => instr.Opcode,
            };
        }

        string?[]? paramNames = null;
        ParamNames.TryGetValue(lookupOp, out paramNames);

        if (rawParams.Length > 0)
        {
            var parts = new List<string>();
            for (int i = 0; i < rawParams.Length; i++)
            {
                string pName = (paramNames != null && i < paramNames.Length && paramNames[i] != null)
                    ? paramNames[i]!
                    : $"p{i}";
                string val = $"0x{rawParams[i]:X}";
                parts.Add($"{pName}={val}");
            }
            sb.Append($"        {name}({string.Join(", ", parts)})");
        }
        else
        {
            sb.Append($"        {name}()");
        }

        if (isAlias)
            sb.Append($"    # op={instr.Opcode}");

        sb.AppendLine();
    }

    // Parameter names per opcode, from wiki opcode-reference.md
    private static readonly Dictionary<uint, string?[]> ParamNames = new()
    {
        [5000] = ["label_id"],
        [5001] = ["message_id", null, "auto_advance", "delay_frames"],
        [5003] = ["dest_var", "operator", "source_var", "value", "value_type"],
        [5004] = ["item_id", "is_add", "quantity", "show_popup", null, "flags"],
        [5006] = ["char_index", "anim_id", "action"],
        [5010] = ["char_id", "action", "level_ref", null, "class_slot"],
        [5012] = ["check_type", "sub_type", "operand", "compare_value", "compare_op", "true_action", "true_label", "false_action", "false_label"],
        [5013] = ["label_id"],
        [5016] = ["entry_id", null, "script_offset", "scene_id", "flags", null, null, "execute_mode", "target_label"],
        [5017] = ["stop_flag", "bgm_id", "play_mode"],
        [5018] = ["play_flags", "loop_count", null, "pos_x", "pos_y", "pos_z"],
        [5020] = ["wait_type", "frame_count"],
        [5021] = ["area_type", "stage_id", "flags", "pos_x", "pos_z", "pos_y", "angle", "entry_point", "transition_param", "npc_ref_id"],
        [5024] = ["step_count"],
        [5025] = ["has_dest", "destination"],
        [5026] = ["fade_type", "duration_frames", "color", null, null, null, null, "alt_enable", "alt_target", "alt_flag"],
        [5027] = ["target_type", "action_id", "flags", "npc_id"],
        [5028] = ["target_type", "move_type", "pos_x", "pos_y", "pos_z", "rotation", "move_param", "npc_id", "move_flags", "callback_var"],
        [5031] = ["message_id", "param1", "param2", "param3", "play_transition", "area_flags"],
        [5035] = ["char_id", "direction"],
        [5036] = ["target_npc_id", "dismiss_flags"],
        [5042] = ["check_type", "item_id", "add_amount", "overflow_label", "flags"],
        [5043] = ["mode", "npc_id", "action", "sub_action"],
        [5044] = ["mode"],
        [5048] = ["layer_id"],
        [5055] = ["char_id", "loop_count"],
        [5059] = ["target_type", "npc_id", "effect_id"],
        [5060] = ["target_type", "npc_id", null, "effect_hash"],
        [5062] = ["mode", null, "transition_frames", "eye_x", "eye_y", "eye_z", "target_x", "target_y", "target_z", "yaw_angle"],
        [5063] = ["flag_index", "direction", "value_or_dest"],
        [5072] = ["target_mode", "char_id", "ability_bitmask"],
        [5079] = ["apply_to_specific", "char_id", "target_type", "target_id", "target_x", "target_y", "target_z"],
        [5083] = ["facing_angle", "flags"],
        [5084] = ["quest_id"],
    };

    // -------------------------------------------------------------------------
    // Waypoints
    // -------------------------------------------------------------------------

    private static void WriteWaypoint(StringBuilder sb, Waypoint wp)
    {
        sb.Append($"waypoint {wp.Id} at ({FormatFloat(wp.PosX)}, {FormatFloat(wp.PosY)}, {FormatFloat(wp.PosZ)})");

        if (wp.Rotation != 0f)
            sb.Append($" rotation={FormatFloat(wp.Rotation)}");

        sb.AppendLine(" {");

        // Condition - only emit if ConditionType is non-zero
        if (wp.ConditionType != 0)
        {
            string condTypeName = wp.ConditionType switch
            {
                2 => "variable",
                3 => "item_count",
                4 => "party_check",
                5 => "flag",
                _ => $"unk_{wp.ConditionType}",
            };
            string condOpName = wp.ConditionOp switch
            {
                0 => "==",
                1 => ">=",
                2 => "<=",
                3 => ">",
                4 => "<",
                5 => "!=",
                _ => $"op_{wp.ConditionOp}",
            };
            sb.AppendLine($"    condition {condTypeName} {wp.ConditionFlags} {condOpName} {wp.ConditionValue}");
        }

        // Resource ref name - byte-swapped ASCII in RefData (swap each 4-byte group)
        if (wp.IsResource)
        {
            var refName = DecodeByteSwappedAscii(wp.RefData);
            if (!string.IsNullOrEmpty(refName))
                sb.AppendLine($"    ref = \"{EscapeString(refName)}\"");
        }

        // Priority for spawn type
        if (wp.IsSpawn)
        {
            // Priority is stored in the low byte of ConditionValue when IsSpawn
            // Use RefData[0..4] as priority (uint)
            uint priority = BigEndian.ReadUInt32(wp.RefData, 0);
            if (priority != 0)
                sb.AppendLine($"    priority = {priority}");
        }

        // @ref_data for unknown RefData that isn't resource/spawn
        if (!wp.IsResource && !wp.IsSpawn)
        {
            var refData = wp.RefData;
            if (refData.Any(b => b != 0))
                sb.AppendLine($"    @ref_data {Convert.ToHexString(refData)}");
        }

        // targets - emit if any non-zero
        uint t0 = wp.Target0;
        uint t1 = wp.Target1;
        uint t2 = wp.Target2;
        if (t0 != 0 || t1 != 0 || t2 != 0)
            sb.AppendLine($"    targets = [{t0}, {t1}, {t2}]");

        sb.AppendLine("}");
    }

    private static string DecodeByteSwappedAscii(byte[] data)
    {
        // Swap each 4-byte group then decode as ASCII
        var swapped = new byte[data.Length];
        for (int i = 0; i + 4 <= data.Length; i += 4)
        {
            swapped[i + 0] = data[i + 3];
            swapped[i + 1] = data[i + 2];
            swapped[i + 2] = data[i + 1];
            swapped[i + 3] = data[i + 0];
        }
        // Trim null terminator
        int end = Array.IndexOf(swapped, (byte)0);
        if (end < 0) end = swapped.Length;
        return System.Text.Encoding.ASCII.GetString(swapped, 0, end);
    }

    // -------------------------------------------------------------------------
    // Helpers (copied from BDS Writer)
    // -------------------------------------------------------------------------

    private static string EntryTypeName(uint type) => type switch
    {
        0 => "spawn",
        1 => "npc",
        2 => "zone",
        3 => "enemy",
        4 => "link",
        5 => "entity",
        6 => "warp",
        _ => $"entry_{type}",
    };

    private static string AreaTypeName(AreaType t) => t switch
    {
        AreaType.Town    => "town",
        AreaType.Indoor  => "indoor",
        AreaType.Dungeon => "dungeon",
        AreaType.World   => "world",
        AreaType.Cube    => "cube",
        _ => $"type_{(uint)t}",
    };

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            uint raw = BitConverter.SingleToUInt32Bits(value);
            return $"0x{raw:X8}";
        }
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool NameRawMatchesName(string name, byte[] nameRaw)
    {
        if (string.IsNullOrEmpty(name)) return nameRaw.All(b => b == 0);
        var encoded = ShiftJisHelper.Encode(name);
        if (encoded.Length > nameRaw.Length) return false;

        for (int i = 0; i < nameRaw.Length; i++)
        {
            byte expected = i < encoded.Length ? encoded[i] : (byte)0;
            if (nameRaw[i] != expected) return false;
        }
        return true;
    }

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
