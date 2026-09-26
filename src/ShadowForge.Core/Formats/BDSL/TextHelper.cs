using System.Globalization;
using System.Text;
using ShadowForge.Scene;

namespace ShadowForge.Formats.BDSL;

public static class TextHelper
{
    /// <summary>
    /// Cuts the line at the first '#' outside double quotes.
    /// </summary>
    public static string StripComment(string line)
    {
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuote = !inQuote;
            if (line[i] == '#' && !inQuote) return line[..i];
        }
        return line;
    }

    /// <summary>
    /// Splits on spaces and braces outside double quotes. Braces are dropped and
    /// quotes are kept on the token.
    /// </summary>
    public static List<string> TokenizeLine(string line)
    {
        var tokens = new List<string>();
        bool inQuote = false;
        var current = new StringBuilder();

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                current.Append(c);
            }
            else if ((c == ' ' || c == '{' || c == '}') && !inQuote)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>
    /// Removes surrounding double quotes and drops the backslash of every escape pair.
    /// </summary>
    public static string UnquoteString(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s.StartsWith('"') && s.EndsWith('"')) s = s[1..^1];
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length) i++;
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parses one 32-bit word written as decimal or 0x-prefixed hex in either case, with an
    /// optional sign. The value must fit in int or uint, and a negative value becomes its
    /// two's complement. A trailing comma is ignored.
    /// </summary>
    public static uint ParseInteger(string s)
    {
        if (!TryParseInteger(s, out uint value))
            throw new FormatException($"not a 32-bit integer: '{s.Trim()}'");
        return value;
    }

    /// <inheritdoc cref="ParseInteger"/>
    public static bool TryParseInteger(ReadOnlySpan<char> s, out uint value)
    {
        value = 0;
        s = s.Trim();
        if (s.EndsWith(",")) s = s[..^1].TrimEnd();
        bool negative = s.Length > 0 && s[0] == '-';
        if (s.Length > 0 && (s[0] == '-' || s[0] == '+')) s = s[1..];

        bool hex = s.Length > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X');
        if (!ulong.TryParse(hex ? s[2..] : s, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None,
                CultureInfo.InvariantCulture, out ulong magnitude))
            return false;

        if (negative)
        {
            if (magnitude > 0x80000000UL) return false;
            value = unchecked((uint)-(long)magnitude);
            return true;
        }
        if (magnitude > uint.MaxValue) return false;
        value = (uint)magnitude;
        return true;
    }

    /// <summary>
    /// Parses a decimal float, or 0x-prefixed hex in either case holding the raw IEEE bits.
    /// </summary>
    public static float ParseFloat(string s)
    {
        s = s.Trim();
        if (IsHexLiteral(s))
            return BitConverter.UInt32BitsToSingle(ParseInteger(s));
        return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether the text starts with an unsigned 0x or 0X prefix.
    /// </summary>
    public static bool IsHexLiteral(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses "(1.0, 2.0, 3.0)" into its components.
    /// </summary>
    public static float[] ParseFloatTuple(string s)
    {
        s = s.Trim().Trim('(', ')');
        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(p => ParseFloat(p.Trim())).ToArray();
    }

    /// <summary>
    /// Round-trip decimal text, except NaN and infinity, which are written as 0x-prefixed raw bits.
    /// </summary>
    public static string FormatFloat(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            uint raw = BitConverter.SingleToUInt32Bits(value);
            return $"0x{raw:X8}";
        }
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string AreaTypeName(AreaType t) => t switch
    {
        AreaType.Town    => "town",
        AreaType.Indoor  => "indoor",
        AreaType.Dungeon => "dungeon",
        AreaType.World   => "world",
        AreaType.Cube    => "cube",
        _ => $"type_{(uint)t}",
    };

    public static AreaType ParseAreaType(string s) => s.Trim() switch
    {
        "town"    => AreaType.Town,
        "indoor"  => AreaType.Indoor,
        "dungeon" => AreaType.Dungeon,
        "world"   => AreaType.World,
        "cube"    => AreaType.Cube,
        var other => (AreaType)ParseInteger(other.StartsWith("type_", StringComparison.Ordinal) ? other[5..] : other),
    };

    public static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static string EntryTypeName(EntryType type) => type switch
    {
        EntryType.Spawn  => "spawn",
        EntryType.Box    => "box",
        EntryType.Zone   => "zone",
        EntryType.Enemy  => "enemy",
        EntryType.Link   => "link",
        EntryType.Entity => "entity",
        EntryType.Warp   => "warp",
        _ => $"entry_{(uint)type}",
    };

    public static EntryType ParseEntryType(string s) => s.Trim() switch
    {
        "spawn"  => EntryType.Spawn,
        "box"    => EntryType.Box,
        "zone"   => EntryType.Zone,
        "enemy"  => EntryType.Enemy,
        "link"   => EntryType.Link,
        "entity" => EntryType.Entity,
        "warp"   => EntryType.Warp,
        var other => (EntryType)ParseInteger(other.StartsWith("entry_", StringComparison.Ordinal) ? other[6..] : other),
    };
}
