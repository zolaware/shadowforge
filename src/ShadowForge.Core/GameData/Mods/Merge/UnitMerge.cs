namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// One mod's units: a whole version of a file, or that mod's replacement for a contested range.
/// </summary>
internal sealed record ModUnits(string Mod, IReadOnlyList<string> Units);

/// <summary>
/// Stock units StockStart..StockEnd that the listed mods replace with different units. An empty
/// range is an insertion point both mods add to.
/// </summary>
internal sealed record RangeConflict(int StockStart, int StockEnd, IReadOnlyList<ModUnits> Sides);

internal sealed record SequenceMergeResult(IReadOnlyList<string> Units, IReadOnlyList<RangeConflict> Conflicts);

/// <summary>
/// One mod's units identified by key, in file order.
/// </summary>
internal sealed record ModKeyedUnits(string Mod, IReadOnlyList<KeyValuePair<string, string>> Units);

/// <summary>
/// One mod's value for a key, null when the mod drops the unit.
/// </summary>
internal sealed record ModValue(string Mod, string? Value);

/// <summary>
/// A key the listed mods set to different values. Stock is null when the key is new.
/// </summary>
internal sealed record KeyConflict(string Key, string? Stock, IReadOnlyList<ModValue> Sides);

internal sealed record KeyedMergeResult(
    IReadOnlyList<KeyValuePair<string, string>> Units, IReadOnlyList<KeyConflict> Conflicts);

/// <summary>
/// Three-way merges of unit lists against the stock list, shared by every format merger. A unit
/// is whatever a format changes as one piece: a line, a table row, a formation, a wave. Changes
/// by different mods are combined when they touch different units. When two mods change the
/// same units, or insert at the same place, and the results differ, the units are a conflict.
/// </summary>
internal static class UnitMerge
{
    /// <summary>
    /// Merges lists whose units are identified by position, aligning each mod's list with the
    /// stock list by SequenceDiff. A conflicting range keeps its stock units in the merged list.
    /// </summary>
    public static SequenceMergeResult MergeSequences(IReadOnlyList<string> stock, IReadOnlyList<ModUnits> versions)
    {
        var edits = new List<(DiffHunk Hunk, int Version)>();
        for (int v = 0; v < versions.Count; v++)
            foreach (var hunk in SequenceDiff.Compute(stock, versions[v].Units))
                edits.Add((hunk, v));
        edits.Sort((x, y) => FirstPoint(x.Hunk) != FirstPoint(y.Hunk)
            ? FirstPoint(x.Hunk).CompareTo(FirstPoint(y.Hunk))
            : LastPoint(x.Hunk).CompareTo(LastPoint(y.Hunk)));

        var merged = new List<string>();
        var conflicts = new List<RangeConflict>();
        int position = 0;
        int next = 0;
        while (next < edits.Count)
        {
            int end = next + 1;
            int reach = LastPoint(edits[next].Hunk);
            while (end < edits.Count && FirstPoint(edits[end].Hunk) <= reach)
            {
                reach = Math.Max(reach, LastPoint(edits[end].Hunk));
                end++;
            }
            var cluster = edits.GetRange(next, end - next);
            int start = cluster.Min(e => e.Hunk.BaseStart);
            int stop = cluster.Max(e => e.Hunk.BaseEnd);

            merged.AddRange(Slice(stock, position, start));
            var sides = cluster
                .GroupBy(e => e.Version)
                .OrderBy(g => g.Key)
                .Select(g => new ModUnits(versions[g.Key].Mod,
                    ApplyHunks(stock, start, stop, g.Select(e => e.Hunk), versions[g.Key].Units)))
                .ToList();
            if (sides.All(s => s.Units.SequenceEqual(sides[0].Units)))
            {
                merged.AddRange(sides[0].Units);
            }
            else if (sides.All(s => s.Units.Count == stop - start))
            {
                MergeAligned(stock, start, sides, merged, conflicts);
            }
            else
            {
                conflicts.Add(new RangeConflict(start, stop, sides));
                merged.AddRange(Slice(stock, start, stop));
            }
            position = stop;
            next = end;
        }
        merged.AddRange(Slice(stock, position, stock.Count));
        return new SequenceMergeResult(merged, conflicts);
    }

    /// <summary>
    /// Merges lists whose units are identified by key. The merged list keeps stock order, drops
    /// keys a mod removed, and appends new keys in the order the mods introduce them. Keys must
    /// be unique within each list.
    /// </summary>
    public static KeyedMergeResult MergeKeyed(
        IReadOnlyList<KeyValuePair<string, string>> stock, IReadOnlyList<ModKeyedUnits> versions)
    {
        var stockValues = stock.ToDictionary(u => u.Key, u => u.Value);
        var versionValues = versions.Select(v => v.Units.ToDictionary(u => u.Key, u => u.Value)).ToList();

        var keys = stock.Select(u => u.Key).ToList();
        var known = new HashSet<string>(keys);
        foreach (var version in versions)
            foreach (var unit in version.Units)
                if (known.Add(unit.Key)) keys.Add(unit.Key);

        var merged = new List<KeyValuePair<string, string>>();
        var conflicts = new List<KeyConflict>();
        foreach (string key in keys)
        {
            string? stockValue = stockValues.GetValueOrDefault(key);
            var changes = versions
                .Select((v, i) => new ModValue(v.Mod, versionValues[i].GetValueOrDefault(key)))
                .Where(c => c.Value != stockValue)
                .ToList();

            string? value = stockValue;
            if (changes.Count > 0 && changes.All(c => c.Value == changes[0].Value))
                value = changes[0].Value;
            else if (changes.Count > 0)
                conflicts.Add(new KeyConflict(key, stockValue, changes));

            if (value is not null) merged.Add(new(key, value));
        }
        return new KeyedMergeResult(merged, conflicts);
    }

    /// <summary>
    /// True when no key appears twice in the list.
    /// </summary>
    public static bool HasUniqueKeys(IReadOnlyList<KeyValuePair<string, string>> units) =>
        units.Select(u => u.Key).Distinct().Count() == units.Count;

    /// <summary>
    /// Hunks laid on one axis where stock unit i sits at 2i + 1 and the gap before it at 2i, so
    /// an insertion overlaps a change only when it lands strictly inside it, and two insertions
    /// overlap only at the same gap.
    /// </summary>
    private static int FirstPoint(DiffHunk h) => h.IsInsertion ? 2 * h.BaseStart : 2 * h.BaseStart + 1;

    private static int LastPoint(DiffHunk h) => h.IsInsertion ? 2 * h.BaseStart : 2 * h.BaseEnd - 1;

    /// <summary>
    /// Merges a range every mod rewrites unit for unit, comparing each position on its own, so
    /// only the units two mods change differently conflict.
    /// </summary>
    private static void MergeAligned(
        IReadOnlyList<string> stock, int start, List<ModUnits> sides, List<string> merged, List<RangeConflict> conflicts)
    {
        for (int k = 0; k < sides[0].Units.Count; k++)
        {
            string stockUnit = stock[start + k];
            var changers = sides.Where(s => s.Units[k] != stockUnit).ToList();
            if (changers.All(c => c.Units[k] == changers[0].Units[k]))
            {
                merged.Add(changers.Count > 0 ? changers[0].Units[k] : stockUnit);
                continue;
            }
            conflicts.Add(new RangeConflict(start + k, start + k + 1,
                changers.Select(c => new ModUnits(c.Mod, [c.Units[k]])).ToList()));
            merged.Add(stockUnit);
        }
    }

    /// <summary>
    /// Stock units start..stop with one mod's hunks inside that range applied.
    /// </summary>
    private static List<string> ApplyHunks(
        IReadOnlyList<string> stock, int start, int stop, IEnumerable<DiffHunk> hunks, IReadOnlyList<string> other)
    {
        var result = new List<string>();
        int position = start;
        foreach (var hunk in hunks.OrderBy(h => h.BaseStart))
        {
            result.AddRange(Slice(stock, position, hunk.BaseStart));
            result.AddRange(Slice(other, hunk.OtherStart, hunk.OtherEnd));
            position = hunk.BaseEnd;
        }
        result.AddRange(Slice(stock, position, stop));
        return result;
    }

    private static IEnumerable<string> Slice(IReadOnlyList<string> items, int start, int end)
    {
        for (int i = start; i < end; i++)
            yield return items[i];
    }
}
