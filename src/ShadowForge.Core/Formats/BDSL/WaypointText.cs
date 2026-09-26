using System.Text;
using ShadowForge.Scene;
using static ShadowForge.Formats.BDSL.SceneTextSyntax;

namespace ShadowForge.Formats.BDSL;

/// <summary>
/// One waypoint block: waypoint N [type=T] at (x, y, z) [rotation=R] { ... }.
/// </summary>
internal static class WaypointText
{
    private static readonly KeywordTable Types = new(
    [
        ((uint)WaypointType.Normal, "waypoint"),
        ((uint)WaypointType.Resource, "resource"),
        ((uint)WaypointType.Spawn, "spawn"),
    ]);

    private static readonly KeywordTable ConditionTypes = new(
    [
        (2, "variable"), (3, "item_count"), (4, "party_check"), (5, "flag"),
    ]);

    /// <summary>
    /// Reads the block whose header is at line i and returns the index of the line after it.
    /// </summary>
    public static int Read(SceneFile scene, string[] lines, int i)
    {
        var wp = new Waypoint();
        ReadHeader(wp, Clean(lines[i]));

        for (i++; i < lines.Length; i++)
        {
            string line = Clean(lines[i]);
            if (IsBlockEnd(line)) { i++; break; }
            ApplyBodyLine(wp, line);
        }

        scene.Waypoints.Add(wp);
        return i;
    }

    private static void ReadHeader(Waypoint wp, string line)
    {
        int brace = line.LastIndexOf('{');
        if (brace >= 0) line = line[..brace].Trim();
        var tokens = TextHelper.TokenizeLine(line);
        int at = tokens.IndexOf("at");

        if (tokens.Count >= 2)
            wp.Id = TextHelper.ParseInteger(tokens[1]);

        for (int t = 2; t < (at >= 0 ? at : tokens.Count); t++)
        {
            if (tokens[t].StartsWith("type="))
            {
                string type = tokens[t][5..];
                wp.Type = (WaypointType)(Types.TryGetValue(type, out uint value) ? value : TextHelper.ParseInteger(type));
            }
        }

        if (at >= 0)
        {
            var tuple = new StringBuilder();
            bool inTuple = false;
            foreach (var token in tokens.Skip(at + 1))
            {
                if (token.StartsWith("(")) inTuple = true;
                if (!inTuple) continue;
                tuple.Append(token).Append(' ');
                if (token.EndsWith(")")) break;
            }
            string text = tuple.ToString().Trim();
            if (text.Length > 0)
                wp.Position = ParseVector(text);
        }

        foreach (var token in tokens)
        {
            if (token.StartsWith("rotation="))
                wp.Rotation = TextHelper.ParseFloat(token[9..]);
        }
    }

    private static void ApplyBodyLine(Waypoint wp, string line)
    {
        if (line.StartsWith("condition "))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) return;
            uint type = ParseConditionType(parts[1]);
            uint op = CompareOp.Parse(parts[3]);
            wp.ConditionFlags = (type & 0x0F) | ((op & 0x0F) << 4);
            wp.ConditionValue = TextHelper.ParseInteger(parts[4]);
        }
        else if (line.StartsWith("ref = "))
        {
            wp.ResourceName = TextHelper.UnquoteString(line[6..].Trim());
        }
        else if (line.StartsWith("priority = ") || line.StartsWith("priority="))
        {
            TrySplitField(line, out _, out string value);
            wp.Priority = TextHelper.ParseInteger(value);
        }
        else if (line.StartsWith("targets = [") || line.StartsWith("targets=["))
        {
            int open = line.IndexOf('[');
            int close = line.IndexOf(']');
            if (close <= open) return;
            var parts = line[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length > 0) wp.LinkCount = TextHelper.ParseInteger(parts[0]);
            if (parts.Length > 1) wp.NextWaypoint = TextHelper.ParseInteger(parts[1]);
            if (parts.Length > 2) wp.UnkField38 = TextHelper.ParseInteger(parts[2]);
        }
        else if (line.StartsWith('@'))
        {
            ApplyDirective(wp, line);
        }
    }

    /// <summary>
    /// @ref_data sets UnkField20..2C in order. @ref_data_extra sets the words after the
    /// resource name: exactly 8 bytes set UnkField28 and UnkField2C, any other length starts at UnkField24.
    /// </summary>
    private static void ApplyDirective(Waypoint wp, string line)
    {
        var (name, value) = SplitDirective(line);
        switch (name)
        {
            case "@condition_value":
                wp.ConditionValue = TextHelper.ParseInteger(value);
                break;
            case "@unk_field_20":
                wp.UnkField20 = TextHelper.ParseInteger(value);
                break;
            case "@unk_field_24":
                wp.UnkField24 = TextHelper.ParseInteger(value);
                break;
            case "@unk_field_28":
                wp.UnkField28 = TextHelper.ParseInteger(value);
                break;
            case "@unk_field_2c":
                wp.UnkField2C = TextHelper.ParseInteger(value);
                break;
            case "@ref_data_extra":
                var extra = HexToWords(value);
                if (value.Length == 16)
                {
                    wp.UnkField28 = extra[0];
                    wp.UnkField2C = extra[1];
                    break;
                }
                if (extra.Length > 0) wp.UnkField24 = extra[0];
                if (extra.Length > 1) wp.UnkField28 = extra[1];
                if (extra.Length > 2) wp.UnkField2C = extra[2];
                break;
            case "@ref_data":
                var words = HexToWords(value);
                if (words.Length > 0) wp.UnkField20 = words[0];
                if (words.Length > 1) wp.UnkField24 = words[1];
                if (words.Length > 2) wp.UnkField28 = words[2];
                if (words.Length > 3) wp.UnkField2C = words[3];
                break;
        }
    }

    private static uint ParseConditionType(string text)
    {
        if (ConditionTypes.TryGetValue(text, out uint type)) return type;
        return TextHelper.ParseInteger(text.StartsWith("unk_") ? text[4..] : text);
    }

    public static void Write(StringBuilder sb, Waypoint wp)
    {
        sb.Append($"waypoint {wp.Id}");
        if (wp.Type != WaypointType.Normal)
            sb.Append(" type=").Append(Types.TryGetName((uint)wp.Type, out string? typeName) ? typeName : ((uint)wp.Type).ToString());
        sb.Append($" at {FormatVector(wp.Position)}");

        if (wp.Rotation != 0f)
            sb.Append($" rotation={TextHelper.FormatFloat(wp.Rotation)}");

        sb.AppendLine(" {");

        if (wp.ConditionType != 0)
        {
            string type = ConditionTypes.TryGetName(wp.ConditionType, out string? name) ? name : $"unk_{wp.ConditionType}";
            sb.AppendLine($"    condition {type} 0x{wp.ConditionFlags:X} {CompareOp.Name(wp.ConditionOp)} 0x{wp.ConditionValue:X}");
        }
        else if (wp.ConditionValue != 0)
        {
            sb.AppendLine($"    @condition_value 0x{wp.ConditionValue:X}");
        }

        var implied = new Waypoint();
        if (wp.IsResource)
        {
            if (!string.IsNullOrEmpty(wp.ResourceName))
                sb.AppendLine($"    ref = \"{TextHelper.EscapeString(wp.ResourceName)}\"");
            implied.ResourceName = wp.ResourceName;
        }
        else if (wp.IsSpawn && wp.Priority != 0)
        {
            sb.AppendLine($"    priority = {wp.Priority}");
        }

        if (!wp.IsSpawn)
            WriteWordOverride(sb, "20", wp.UnkField20, implied.UnkField20);
        WriteWordOverride(sb, "24", wp.UnkField24, implied.UnkField24);
        WriteWordOverride(sb, "28", wp.UnkField28, implied.UnkField28);
        WriteWordOverride(sb, "2c", wp.UnkField2C, 0);

        uint t0 = wp.LinkCount;
        uint t1 = wp.NextWaypoint;
        uint t2 = wp.UnkField38;
        if (t0 != 0 || t1 != 0 || t2 != 0)
            sb.AppendLine($"    targets = [{t0}, {t1}, {t2}]");

        sb.AppendLine("}");
    }

    /// <summary>
    /// A ref word is written only when it differs from what the ref name (or zero) already implies.
    /// </summary>
    private static void WriteWordOverride(StringBuilder sb, string suffix, uint value, uint implied)
    {
        if (value != implied)
            sb.AppendLine($"    @unk_field_{suffix} 0x{value:X}");
    }
}
