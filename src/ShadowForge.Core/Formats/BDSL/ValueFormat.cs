using System.Globalization;
using ShadowForge.Scene.Script;

namespace ShadowForge.Formats.BDSL;

/// <summary>
/// Formats and parses one argument word according to its kind.
/// </summary>
public static class ValueFormat
{
    private const uint PlainIntLimit = 0x00FFFFFF;

    internal static readonly ArgSpec PlainInt = new("n", ArgKind.Int);
    internal static readonly ArgSpec Character = new("chara", ArgKind.Enum, OpcodeTable.CharNames);

    /// <summary>
    /// Text for a value under the argument's kind. Int values above 0xFFFFFF print as hex.
    /// </summary>
    public static string Format(uint value, ArgSpec spec) => spec.Kind switch
    {
        ArgKind.Hex => "0x" + value.ToString("X", CultureInfo.InvariantCulture),
        ArgKind.Float => FormatFloatBits(value),
        ArgKind.Var => $"var[{value}]",
        ArgKind.Label => $"@{value}",
        ArgKind.Enum when spec.EnumNames is not null && spec.EnumNames.TryGetValue(value, out var name) => name,
        _ => value > PlainIntLimit ? "0x" + value.ToString("X", CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Shortest decimal that round-trips the float, or hex for NaN, infinity and denormals.
    /// </summary>
    public static string FormatFloatBits(uint bits)
    {
        float f = BitConverter.UInt32BitsToSingle(bits);
        bool plain = bits == 0 || (float.IsFinite(f) && float.IsNormal(f));
        if (!plain) return "0x" + bits.ToString("X", CultureInfo.InvariantCulture);
        string s = f.ToString("R", CultureInfo.InvariantCulture);
        if (!s.Contains('.') && !s.Contains('E')) s += ".0";
        return s;
    }

    /// <summary>
    /// Accepts every form Format writes, and any integer in the spellings
    /// <see cref="TextHelper.ParseInteger"/> reads. The kind only decides how a bare
    /// decimal is read: a float argument reads it as a float, anything else as an integer.
    /// </summary>
    public static uint Parse(string token, ArgSpec spec)
    {
        token = token.Trim();
        if (token.StartsWith("var[", StringComparison.Ordinal) && token.EndsWith(']'))
            return TextHelper.ParseInteger(token[4..^1]);
        if (token.StartsWith('@'))
            return TextHelper.ParseInteger(token[1..]);
        if (spec.EnumNames is not null)
            foreach (var pair in spec.EnumNames)
                if (pair.Value == token) return pair.Key;
        foreach (var pair in OpcodeTable.CharNames)
            if (pair.Value == token) return pair.Key;
        if (spec.Kind == ArgKind.Float && !TextHelper.IsHexLiteral(token))
        {
            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                return BitConverter.SingleToUInt32Bits(f);
            throw new FormatException($"not a float: '{token}'");
        }
        if (TextHelper.TryParseInteger(token, out uint value))
            return value;
        throw new FormatException($"not a value: '{token}'");
    }
}
