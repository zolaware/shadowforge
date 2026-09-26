using System.Numerics;
using System.Text;
using ShadowForge.Scene;
using static ShadowForge.Formats.BDSL.SceneTextSyntax;

namespace ShadowForge.Formats.BDSL;

/// <summary>
/// One entry block: keyword "name" id=N { fields, @directives, when-blocks }.
/// Extents and TriggerRadius are spelled per entry type (extents/yaw for a box,
/// route/aggro_radius for an enemy, rotation for a warp, raw @target_refs/@field_44 otherwise).
/// </summary>
internal static class EntryText
{
    /// <summary>
    /// Whether the line opens an entry: a named entry type, or entry_N for any other type.
    /// </summary>
    public static bool IsHeader(string line)
    {
        int space = line.IndexOf(' ');
        if (space <= 0) return false;
        string keyword = line[..space];
        return Enum.GetValues<EntryType>().Any(type => TextHelper.EntryTypeName(type) == keyword)
            || (keyword.StartsWith("entry_", StringComparison.Ordinal) && TextHelper.TryParseInteger(keyword.AsSpan(6), out _));
    }

    /// <summary>
    /// Reads the block whose header is at line i and returns the index of the line after it.
    /// </summary>
    public static int Read(SceneFile scene, string[] lines, int i)
    {
        var tokens = TextHelper.TokenizeLine(Clean(lines[i]));
        var entry = new SceneEntry { Type = TextHelper.ParseEntryType(tokens[0]) };
        if (tokens.Count >= 2)
            entry.Name = TextHelper.UnquoteString(tokens[1]);
        foreach (var token in tokens.Skip(2))
        {
            if (token.StartsWith("id="))
                entry.Id = TextHelper.ParseInteger(token[3..]);
        }

        i++;
        while (i < lines.Length)
        {
            string line = Clean(lines[i]);
            if (IsBlockEnd(line)) { i++; break; }

            if (line.StartsWith("when "))
            {
                i = WhenBlockText.Read(entry, lines, i);
                continue;
            }

            if (line.StartsWith("@"))
                ApplyDirective(entry, line);
            else if (TrySplitField(line, out string key, out string value))
                ApplyField(entry, key, value);
            i++;
        }

        scene.Entries.Add(entry);
        return i;
    }

    private static void ApplyField(SceneEntry entry, string key, string value)
    {
        switch (key)
        {
            case "position":
                entry.Position = ParseVector(value);
                break;
            case "facing":
                entry.Facing = TextHelper.ParseFloat(value);
                break;
            case "ref_id":
                entry.RefId = TextHelper.ParseInteger(value);
                break;
            case "extents":
            case "route":
                entry.Extents = ParseVector(value);
                break;
            case "yaw":
            case "aggro_radius":
            case "rotation":
                entry.TriggerRadius = TextHelper.ParseFloat(value);
                break;
        }
    }

    private static void ApplyDirective(SceneEntry entry, string line)
    {
        var (name, value) = SplitDirective(line);
        switch (name)
        {
            case "@padding":
                var pad = HexToWords(value);
                if (pad.Length > 0) entry.UnkField48 = pad[0];
                if (pad.Length > 1) entry.UnkField4C = pad[1];
                if (pad.Length > 2) entry.UnkField50 = pad[2];
                if (pad.Length > 3) entry.UnkField54 = pad[3];
                break;
            case "@overlap":
                var overlap = HexToWords(value);
                if (overlap.Length > 0) entry.UnkField58 = overlap[0];
                if (overlap.Length > 1) entry.UnkField5C = overlap[1];
                if (overlap.Length > 2) entry.UnkField60 = overlap[2];
                break;
            case "@target_refs":
                var refs = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                entry.Extents = new Vector3(TargetRef(refs, 0), TargetRef(refs, 1), TargetRef(refs, 2));
                break;
            case "@field_44":
                entry.TriggerRadius = TextHelper.ParseFloat(value);
                break;
            case "@runtime_ref":
                entry.RuntimeRef = TextHelper.ParseInteger(value);
                break;
        }
    }

    private static float TargetRef(string[] refs, int index) =>
        refs.Length > index ? BitConverter.UInt32BitsToSingle(TextHelper.ParseInteger(refs[index])) : 0f;

    public static void Write(StringBuilder sb, SceneEntry entry)
    {
        sb.AppendLine($"{TextHelper.EntryTypeName(entry.Type)} \"{TextHelper.EscapeString(entry.Name)}\" id={entry.Id} {{");

        if (entry.RuntimeRef != 0)
            sb.AppendLine($"    @runtime_ref 0x{entry.RuntimeRef:X}");

        if (entry.RefId != 0)
            sb.AppendLine($"    ref_id = 0x{entry.RefId:X}");

        if (!IsZero(entry.Position))
            sb.AppendLine($"    position = {FormatVector(entry.Position)}");

        if (entry.Facing != 0f)
            sb.AppendLine($"    facing = {TextHelper.FormatFloat(entry.Facing)}");

        switch (entry.Type)
        {
            case EntryType.Box:
                if (!IsZero(entry.Extents))
                    sb.AppendLine($"    extents = {FormatVector(entry.Extents)}");
                if (entry.TriggerRadius != 0f)
                    sb.AppendLine($"    yaw = {TextHelper.FormatFloat(entry.TriggerRadius)}");
                break;

            case EntryType.Enemy:
                if (!IsZero(entry.Extents))
                    sb.AppendLine($"    route = {FormatVector(entry.Extents)}");
                if (entry.TriggerRadius != 0f)
                    sb.AppendLine($"    aggro_radius = {TextHelper.FormatFloat(entry.TriggerRadius)}");
                break;

            case EntryType.Warp:
                if (entry.TriggerRadius != 0f)
                    sb.AppendLine($"    rotation = {TextHelper.FormatFloat(entry.TriggerRadius)}");
                break;

            default:
                uint t0 = BitConverter.SingleToUInt32Bits(entry.Extents.X);
                uint t1 = BitConverter.SingleToUInt32Bits(entry.Extents.Y);
                uint t2 = BitConverter.SingleToUInt32Bits(entry.Extents.Z);
                if (t0 != 0 || t1 != 0 || t2 != 0)
                    sb.AppendLine($"    @target_refs 0x{t0:X} 0x{t1:X} 0x{t2:X}");
                if (entry.TriggerRadius != 0f)
                    sb.AppendLine($"    @field_44 {TextHelper.FormatFloat(entry.TriggerRadius)}");
                break;
        }

        if (entry.UnkField48 != 0 || entry.UnkField4C != 0 || entry.UnkField50 != 0 || entry.UnkField54 != 0)
            sb.AppendLine($"    @padding {WordsToHex(entry.UnkField48, entry.UnkField4C, entry.UnkField50, entry.UnkField54)}");

        if (entry.UnkField58 != 0 || entry.UnkField5C != 0 || entry.UnkField60 != 0)
            sb.AppendLine($"    @overlap {WordsToHex(entry.UnkField58, entry.UnkField5C, entry.UnkField60)}");

        foreach (var block in entry.ScriptBlocks)
            WhenBlockText.Write(sb, block);

        sb.AppendLine("}");
    }
}
