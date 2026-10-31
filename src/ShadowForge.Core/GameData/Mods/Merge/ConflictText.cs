using System.Security.Cryptography;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Labels and one-line renderings that format mergers use to describe conflicts, plus the
/// content fingerprint they compare binary units by.
/// </summary>
internal static class ConflictText
{
    private const int MaxLength = 160;

    public const string Removed = "(removed)";
    public const string NotInStock = "(not in the game)";

    /// <summary>
    /// Units joined on one line with their terminators dropped, cut to a readable length.
    /// </summary>
    public static string Join(IEnumerable<string> units)
    {
        string text = string.Join(" / ", units.Select(u => u.TrimEnd('\r', '\n').Trim()));
        if (text.Length == 0) return Removed;
        return text.Length <= MaxLength ? text : text[..MaxLength] + "...";
    }

    /// <summary>
    /// "line 12", "lines 12-14", or "new lines after line 11" for an insertion, counting from 1.
    /// </summary>
    public static string Range(string noun, int start, int end)
    {
        if (start == end)
            return start == 0 ? $"new {noun}s at the start" : $"new {noun}s after {noun} {start}";
        return end - start == 1 ? $"{noun} {start + 1}" : $"{noun}s {start + 1}-{end}";
    }

    /// <summary>
    /// Turns a positional conflict into a report entry, rendering each range with describe.
    /// </summary>
    public static MergeConflict FromRange(
        string noun, RangeConflict conflict, IReadOnlyList<string> stock, Func<IEnumerable<string>, string> describe)
    {
        var stockUnits = stock.Skip(conflict.StockStart).Take(conflict.StockEnd - conflict.StockStart);
        return new MergeConflict(
            Range(noun, conflict.StockStart, conflict.StockEnd),
            conflict.StockStart == conflict.StockEnd ? "(nothing here)" : describe(stockUnits),
            conflict.Sides.Select(s => new ConflictSide(s.Mod, s.Units.Count == 0 ? Removed : describe(s.Units))).ToList());
    }

    /// <summary>
    /// Turns a keyed conflict into a report entry, rendering each value with describe.
    /// </summary>
    public static MergeConflict FromKey(string location, KeyConflict conflict, Func<string, string> describe) =>
        new(location,
            conflict.Stock is null ? NotInStock : describe(conflict.Stock),
            conflict.Sides.Select(s => new ConflictSide(s.Mod, s.Value is null ? Removed : describe(s.Value))).ToList());

    public static string Fingerprint(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
