using System.Text;
using ShadowForge.Scene;
using ShadowForge.Scene.Script;
using ShadowForge.Scene.Script.Lift;
using static ShadowForge.Formats.BDSL.SceneTextSyntax;

namespace ShadowForge.Formats.BDSL;

/// <summary>
/// One script block: when [conditions] { @metadata, statements, param_data }.
/// Metadata directives and param_data are only recognized at the block's own
/// nesting depth. Everything else is handed to <see cref="StatementParser"/>.
/// </summary>
internal static class WhenBlockText
{
    private static readonly KeywordTable BlockTypes = new(
    [
        (1, "event"), (2, "npc"), (3, "auto_run"), (4, "link"), (5, "cube"),
    ]);

    private static readonly KeywordTable Encounters = new([(1, "normal"), (2, "boss")]);

    private static readonly KeywordTable Behaviors = new(
    [
        ((uint)BehaviorMode.Default, "default"),
        ((uint)BehaviorMode.Alternate, "alternate"),
        (unchecked((uint)BehaviorMode.Disabled), "disabled"),
    ]);

    private static readonly KeywordTable RenderFlags = new(
    [
        (0x1, "visible"),
        (0x2, "type_b"),
        (0x10, "variant_b"),
        (0x20, "variant_a"),
        (0x08000000, "special_anim"),
        (0x10000000, "collision"),
        (0x80000000, "has_model"),
    ]);

    /// <summary>
    /// Header words with no known meaning, written as "@raw_header offset value" by block offset.
    /// </summary>
    private static readonly (uint Offset, Func<ScriptBlock, uint> Get, Action<ScriptBlock, uint> Set)[] RawHeaderWords =
    [
        (0x88, b => b.UnkField88, (b, v) => b.UnkField88 = v),
        (0x8C, b => b.UnkField8C, (b, v) => b.UnkField8C = v),
        (0xA4, b => b.UnkFieldA4, (b, v) => b.UnkFieldA4 = v),
        (0xA8, b => b.UnkFieldA8, (b, v) => b.UnkFieldA8 = v),
        (0xAC, b => b.UnkFieldAC, (b, v) => b.UnkFieldAC = v),
        (0xB0, b => b.UnkFieldB0, (b, v) => b.UnkFieldB0 = v),
        (0xB4, b => b.UnkFieldB4, (b, v) => b.UnkFieldB4 = v),
        (0xB8, b => b.UnkFieldB8, (b, v) => b.UnkFieldB8 = v),
        (0xBC, b => b.UnkFieldBC, (b, v) => b.UnkFieldBC = v),
        (0xC0, b => b.UnkFieldC0, (b, v) => b.UnkFieldC0 = v),
    ];

    /// <summary>
    /// Reads the block whose header is at line i and returns the index of the line after it.
    /// Directive and param_data lines are replaced by blank lines in the statement body so
    /// statement errors still report the right line number.
    /// </summary>
    public static int Read(SceneEntry entry, string[] lines, int i)
    {
        var block = new ScriptBlock();
        string header = Clean(lines[i])[4..].Trim();
        if (!header.EndsWith('{')) throw LineError(i + 1, "when-block header must end with '{'");
        try { BlockConditionText.Parse(block, header[..^1].Trim()); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw LineError(i + 1, ex.Message);
        }

        int bodyStart = i + 1;
        var body = new List<string>();
        int depth = 0;
        for (i++; i < lines.Length; i++)
        {
            string raw = lines[i].TrimEnd('\r');
            string line = raw.Trim().StartsWith("//", StringComparison.Ordinal) ? "" : Clean(raw);

            if (depth == 0 && line == "}") { i++; break; }
            if (depth == 0 && line.StartsWith('@') && !line.EndsWith(':'))
            {
                ApplyDirective(block, line);
                body.Add("");
                continue;
            }
            if (depth == 0 && line.StartsWith("param_data", StringComparison.Ordinal))
            {
                string hex = line[10..].Trim();
                try { block.ParamData = hex.Length > 0 ? Convert.FromHexString(hex) : []; }
                catch (Exception ex) when (ex is FormatException or ArgumentException)
                {
                    throw LineError(i + 1, ex.Message);
                }
                body.Add("");
                continue;
            }
            if (line.EndsWith('{')) depth++;
            else if (line == "}") depth--;
            body.Add(raw);
        }

        block.Elements = ScriptLowering.Lower(StatementParser.ParseBlock(body, bodyStart + 1));
        entry.ScriptBlocks.Add(block);
        return i;
    }

    private static void ApplyDirective(ScriptBlock block, string line)
    {
        var (name, value) = SplitDirective(line);
        switch (name)
        {
            case "@block_type":
                block.BlockType = Named(BlockTypes, value);
                break;
            case "@render":
                uint flags = 0;
                foreach (var flag in value.Split(',', StringSplitOptions.TrimEntries))
                    flags |= Named(RenderFlags, flag);
                block.RenderFlags = flags;
                break;
            case "@behavior":
                block.BehaviorMode = (BehaviorMode)(int)Named(Behaviors, value);
                break;
            case "@auto_run":
                block.AutoRunFlags = TextHelper.ParseInteger(value);
                break;
            case "@encounter":
                block.EncounterMode = Named(Encounters, value);
                break;
            case "@encounter_range":
                block.EncounterRange = TextHelper.ParseFloat(value);
                break;
            case "@auto_set_var":
                block.AutoSetVarFlag = 1;
                block.AutoSetVarIndex = TextHelper.ParseInteger(value);
                break;
            case "@auto_set_var_index":
                block.AutoSetVarIndex = TextHelper.ParseInteger(value);
                break;
            case "@linked_entry":
                block.LinkedEntryId = TextHelper.ParseInteger(value);
                break;
            case "@spawn_config":
            case "@spawn_flags":
                block.SpawnFlags = TextHelper.ParseInteger(value);
                break;
            case "@spawn_angle":
                block.SpawnAngle = TextHelper.ParseFloat(value);
                break;
            case "@spawn_scale":
                block.SpawnScale = TextHelper.ParseFloat(value);
                break;
            case "@interaction_radius":
                block.InteractionRadius = TextHelper.ParseFloat(value);
                break;
            case "@type_data":
                block.TypeData = TextHelper.ParseInteger(value);
                break;
            case "@spawn_mode":
                block.SpawnMode = TextHelper.ParseInteger(value);
                break;
            case "@raw_header":
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) break;
                uint offset = TextHelper.ParseInteger(parts[0]);
                uint raw = TextHelper.ParseInteger(parts[1]);
                foreach (var word in RawHeaderWords)
                {
                    if (word.Offset == offset)
                        word.Set(block, raw);
                }
                break;
        }
    }

    private static uint Named(KeywordTable table, string text) =>
        table.TryGetValue(text, out uint value) ? value : TextHelper.ParseInteger(text);

    public static void Write(StringBuilder sb, ScriptBlock block)
    {
        bool hasChapterRange = block.ChapterMin != 0 || block.ChapterMax != 0;
        bool hasMetadata = block.HasAnyMetadata || !block.ReservedRegionsAreZero;
        bool hasContent = block.Elements.Count > 0 || block.ParamData.Length > 0;
        if (!hasChapterRange && block.HasNoConditions && !hasMetadata && !hasContent)
            return;

        sb.AppendLine();
        string header = BlockConditionText.Format(block);
        sb.AppendLine(header.Length == 0 ? "    when {" : $"    when {header} {{");

        WriteMetadata(sb, block);

        if (!block.ReservedRegionsAreZero)
        {
            foreach (var word in RawHeaderWords)
            {
                uint value = word.Get(block);
                if (value != 0)
                    sb.AppendLine($"        @raw_header 0x{word.Offset:X} 0x{value:X8}");
            }
        }

        StatementWriter.Write(sb, ScriptLifter.Lift(block.Elements), 8);

        if (block.ParamData.Length > 0)
            sb.AppendLine($"        param_data {Convert.ToHexString(block.ParamData)}");

        sb.AppendLine("    }");
    }

    private static void WriteMetadata(StringBuilder sb, ScriptBlock block)
    {
        if (block.BlockType != 0)
            sb.AppendLine($"        @block_type {NameOrHex(BlockTypes, block.BlockType)}");

        if (block.RenderFlags != 0)
            sb.AppendLine($"        @render {string.Join(", ", RenderFlagNames(block.RenderFlags))}");

        if (block.BehaviorMode != BehaviorMode.None)
        {
            string behavior = Behaviors.TryGetName((uint)block.BehaviorMode, out string? name)
                ? name
                : ((int)block.BehaviorMode).ToString();
            sb.AppendLine($"        @behavior {behavior}");
        }

        if (block.AutoRunFlags != 0)
            sb.AppendLine($"        @auto_run 0x{block.AutoRunFlags:X}");

        if (block.EncounterMode != 0)
            sb.AppendLine($"        @encounter {NameOrHex(Encounters, block.EncounterMode)}");

        if (block.EncounterRange != 0f)
            sb.AppendLine($"        @encounter_range {TextHelper.FormatFloat(block.EncounterRange)}");

        if (block.AutoSetVarFlag != 0)
            sb.AppendLine($"        @auto_set_var {block.AutoSetVarIndex}");
        else if (block.AutoSetVarIndex != 0)
            sb.AppendLine($"        @auto_set_var_index {block.AutoSetVarIndex}");

        if (block.LinkedEntryId != 0)
            sb.AppendLine($"        @linked_entry {block.LinkedEntryId}");

        if (block.SpawnFlags != 0)
            sb.AppendLine($"        @spawn_flags 0x{block.SpawnFlags:X}");

        if (block.SpawnAngle != 0f)
            sb.AppendLine($"        @spawn_angle {TextHelper.FormatFloat(block.SpawnAngle)}");

        if (block.SpawnScale != 0f)
            sb.AppendLine($"        @spawn_scale {TextHelper.FormatFloat(block.SpawnScale)}");

        if (block.InteractionRadius != 0f)
            sb.AppendLine($"        @interaction_radius {TextHelper.FormatFloat(block.InteractionRadius)}");

        if (block.TypeData != 0)
            sb.AppendLine($"        @type_data 0x{block.TypeData:X}");

        if (block.SpawnMode != 0)
            sb.AppendLine($"        @spawn_mode 0x{block.SpawnMode:X}");
    }

    private static string NameOrHex(KeywordTable table, uint value) =>
        table.TryGetName(value, out string? name) ? name : $"0x{value:X}";

    /// <summary>
    /// Known flag names in bit order, then any unknown bits as one hex value.
    /// </summary>
    private static List<string> RenderFlagNames(uint flags)
    {
        var names = new List<string>();
        uint known = 0;
        foreach (var (bit, name) in RenderFlags.Entries)
        {
            known |= bit;
            if ((flags & bit) != 0) names.Add(name);
        }
        uint unknown = flags & ~known;
        if (unknown != 0)
            names.Add($"0x{unknown:X}");
        return names;
    }
}
