
namespace ShadowForge.Formats.BDSL;

/// <summary>
/// The six compare operators, by their index in script words.
/// </summary>
public static class CompareOp
{
    private static readonly string[] Names = ["==", ">=", "<=", ">", "<", "!="];

    /// <summary>
    /// Spelling of operator 0..5, or op_N when out of range.
    /// </summary>
    public static string Name(uint op) => op < Names.Length ? Names[op] : $"op_{op}";

    public static bool TryGetIndex(string text, out uint op)
    {
        int index = Array.IndexOf(Names, text);
        op = index < 0 ? 0 : (uint)index;
        return index >= 0;
    }

    /// <summary>
    /// Inverse of <see cref="Name"/> that also accepts a bare number.
    /// </summary>
    public static uint Parse(string text)
    {
        if (TryGetIndex(text, out uint op)) return op;
        return TextHelper.ParseInteger(text.StartsWith("op_") ? text[3..] : text);
    }
}
