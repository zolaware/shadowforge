using System.Text;
using System.Text.RegularExpressions;
using ShadowForge.Scene.Script;

namespace ShadowForge.Formats.BDSL;

/// <summary>
/// Formats and parses the condition header of a when-block.
/// </summary>
public static class BlockConditionText
{
    private const uint Unconditional = 0xFFFFFFFF;
    private const int PartySize = 5;

    /// <summary>
    /// Header text between 'when' and '{'. Empty for ChapterMin 0xFFFFFFFF, ChapterMax 0 and no conditions.
    /// </summary>
    public static string Format(ScriptBlock block)
    {
        var parts = new List<string>();
        if (block.ChapterMin == Unconditional)
        {
            if (block.ChapterMax != 0) parts.Add($"all..{block.ChapterMax}");
        }
        else
        {
            parts.Add($"chapter {block.ChapterMin}..{block.ChapterMax}");
        }
        foreach (var cond in block.Conditions)
        {
            if (cond.IsEmpty) continue;
            parts.Add(Slot(cond));
        }
        return string.Join(" and ", parts);
    }

    private static string Slot(Condition c)
    {
        switch (c.Type)
        {
            case 2 when c.Op <= 5:
                return $"var[{c.Operand}] {CompareOp.Name(c.Op)} {c.Value}";
            case 3 when c.Op <= 1:
                return $"item_count({c.Operand}) {(c.Op == 0 ? "<" : ">=")} {c.Value}";
            case 4 when c.Op <= 1 && c.Value == 0 && (c.Operand & ~0x1Fu) == 0:
                var names = new List<string>();
                for (int bit = 0; bit < PartySize; bit++)
                    if ((c.Operand & (1u << bit)) != 0) names.Add(OpcodeTable.CharNames[(uint)bit]);
                return $"{(c.Op == 1 ? "party_all" : "party_missing")}({string.Join(", ", names)})";
            case 5 when c.Operand == 2 && c.Op == 0:
                return $"savebit({c.Value})";
            case 5 when c.Operand == 2 && c.Op == 1 && c.Value == 0:
                return "global_4095";
            default:
                return $"cond({c.Type}, {c.Operand}, {c.Op}, {c.Value})";
        }
    }

    /// <summary>
    /// Fills the chapter range and condition slots. Parts are joined by ' and ' or ','.
    /// </summary>
    public static void Parse(ScriptBlock block, string header)
    {
        block.ChapterMin = Unconditional;
        block.ChapterMax = 0;
        Array.Clear(block.Conditions);
        int slot = 0;
        foreach (var part in SplitTopLevel(header))
        {
            if (part == "all") continue;
            if (part.StartsWith("all..", StringComparison.Ordinal)) { block.ChapterMax = Num(part[5..]); continue; }
            if (part.StartsWith("chapter ", StringComparison.Ordinal))
            {
                var range = part[8..].Split("..");
                block.ChapterMin = Num(range[0]);
                block.ChapterMax = range.Length > 1 ? Num(range[1]) : block.ChapterMin;
                continue;
            }
            if (slot >= block.Conditions.Length) throw new FormatException("more than 8 block conditions");
            block.Conditions[slot++] = ParseSlot(part);
        }
    }

    private static List<string> SplitTopLevel(string header)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        for (int i = 0; i < header.Length; i++)
        {
            char ch = header[i];
            if (ch == '(') depth++;
            else if (ch == ')') depth--;
            bool andHere = depth == 0 && string.CompareOrdinal(header, i, " and ", 0, 5) == 0;
            if (depth == 0 && (ch == ',' || andHere))
            {
                parts.Add(current.ToString());
                current.Clear();
                if (andHere) i += 4;
                continue;
            }
            current.Append(ch);
        }
        parts.Add(current.ToString());
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    private static Condition ParseSlot(string part)
    {
        var m = Regex.Match(part, @"^var\[([^\]]+)\]\s*(==|>=|<=|!=|>|<)\s*(\S+)$");
        if (m.Success)
        {
            CompareOp.TryGetIndex(m.Groups[2].Value, out uint op);
            return new Condition(2, Num(m.Groups[1].Value), op, Num(m.Groups[3].Value));
        }
        m = Regex.Match(part, @"^item_count\((\S+)\)\s*(<|>=)\s*(\S+)$");
        if (m.Success) return new Condition(3, Num(m.Groups[1].Value), m.Groups[2].Value == "<" ? 0u : 1u, Num(m.Groups[3].Value));
        m = Regex.Match(part, @"^(party_all|party_missing)\((.*)\)$");
        if (m.Success)
        {
            uint mask = 0;
            foreach (var name in m.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                mask |= 1u << PartyBit(name);
            return new Condition(4, mask, m.Groups[1].Value == "party_all" ? 1u : 0u, 0);
        }
        m = Regex.Match(part, @"^savebit\((\S+)\)$");
        if (m.Success) return new Condition(5, 2, 0, Num(m.Groups[1].Value));
        if (part == "global_4095") return new Condition(5, 2, 1, 0);
        m = Regex.Match(part, @"^cond\((\S+),\s*(\S+),\s*(\S+),\s*(\S+)\)$");
        if (m.Success) return new Condition(Num(m.Groups[1].Value), Num(m.Groups[2].Value), Num(m.Groups[3].Value), Num(m.Groups[4].Value));
        throw new FormatException($"cannot parse block condition '{part}'");
    }

    private static int PartyBit(string name)
    {
        foreach (var (index, member) in OpcodeTable.CharNames)
            if (member == name && index < PartySize) return (int)index;
        throw new FormatException($"unknown party member '{name}'");
    }

    private static uint Num(string token) => ValueFormat.Parse(token, ValueFormat.PlainInt);
}
