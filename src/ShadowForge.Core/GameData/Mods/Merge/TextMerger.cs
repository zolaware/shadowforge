namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Merges text tables and definition files line by line, so each table row (an item, skill,
/// enemy, shop slot) is one unit. Tables the DLC grows by inserting rows are merged on their id
/// column instead, so rows a mod inserts do not shift the rows after them. In a table identified
/// by position, two mods appending different rows both claim the same new row ids, which is a
/// conflict like any other. Conflicting table rows are reported by what they are and by the
/// columns each mod changes.
/// </summary>
internal sealed class TextMerger(GameNames names) : IFormatMerger
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".u16", ".csv", ".txt", ".mdl", ".map", ".xml", ".ini", ".toml", ".bdsl",
        ".ara", ".blo", ".cam", ".dlt", ".efc", ".efp", ".fog", ".gml", ".hca", ".hda",
        ".lip", ".lpa", ".mmp", ".pst", ".scn", ".ss",
    };

    /// <summary>
    /// Tables whose rows the game finds by id, with the column holding it. Shuffle Dungeon's
    /// SkTb_new carries 1472 rows against the base 1235, so a row's position is not its identity.
    /// </summary>
    private static readonly Dictionary<string, int> IdColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sktb_new.u16"] = 17,
        ["sktb.u16"] = 17,
        ["skef_new.u16"] = 0,
        ["skef.u16"] = 0,
        ["itef.u16"] = 0,
    };

    public string Method => "line by line";

    public static bool Handles(string fileName) => TextExtensions.Contains(Path.GetExtension(fileName));

    public FileMergeResult Merge(string fileName, byte[] stock, IReadOnlyList<ModVersion> versions)
    {
        var encoding = TextLines.DetectEncoding(fileName, stock);
        var stockText = TextLines.Decode(stock, encoding);
        if (stockText is null)
            return FileMergeResult.Unmergeable(Method, $"the shipped copy is not valid {encoding} text");

        var texts = new List<(string Mod, TextLines Text)>();
        foreach (var version in versions)
        {
            var text = TextLines.Decode(version.Data, encoding);
            if (text is null)
                return FileMergeResult.Unmergeable(Method, $"{version.Mod}'s copy is not valid {encoding} text");
            texts.Add((version.Mod, text));
        }

        var layout = TextLayout.Merge(stockText.Layout, texts.Select(t => t.Text.Layout).ToList());
        var rows = TableRowDescriber.IsTable(fileName) ? new TableRowDescriber(fileName, stockText.Lines, names) : null;

        if (IdColumns.TryGetValue(Path.GetFileName(fileName), out int column))
        {
            var rowMerge = MergeRowsById(stockText.Lines, texts, column, rows);
            if (rowMerge is not null)
            {
                string method = $"row by row on id column {column}";
                return rowMerge.Value.Conflicts.Count > 0
                    ? FileMergeResult.Conflicted(method, rowMerge.Value.Conflicts)
                    : FileMergeResult.Merged(TextLines.Encode(rowMerge.Value.Lines, layout, encoding), method);
            }
        }

        var merged = UnitMerge.MergeSequences(stockText.Lines,
            texts.Select(t => new ModUnits(t.Mod, t.Text.Lines)).ToList());
        if (merged.Conflicts.Count > 0)
            return FileMergeResult.Conflicted(Method, merged.Conflicts
                .Select(c => rows is null
                    ? ConflictText.FromRange("line", c, stockText.Lines, ConflictText.Join)
                    : DescribeRows(c, stockText.Lines, rows))
                .ToList());
        return FileMergeResult.Merged(TextLines.Encode(merged.Units, layout, encoding), Method);
    }

    /// <summary>
    /// A conflicting table range: one changed row is named and each mod's column changes are
    /// listed; rows added or removed are listed by label.
    /// </summary>
    private static MergeConflict DescribeRows(RangeConflict conflict, IReadOnlyList<string> stock, TableRowDescriber rows)
    {
        int start = conflict.StockStart;
        if (conflict.StockEnd - start == 1 && conflict.Sides.All(s => s.Units.Count == 1))
            return new MergeConflict(
                rows.RowLabel(start, stock[start]), "",
                conflict.Sides.Select(s => new ConflictSide(s.Mod, rows.FieldChanges(stock[start], s.Units[0]))).ToList());

        string location = start == conflict.StockEnd
            ? (start == 0 ? "new rows at the start" : $"new rows after {rows.RowLabel(start - 1, stock[start - 1])}")
            : $"{rows.RowLabel(start, stock[start])} to {rows.RowLabel(conflict.StockEnd - 1, stock[conflict.StockEnd - 1])}";
        return new MergeConflict(location,
            start == conflict.StockEnd ? "" : RowList(stock.Skip(start).Take(conflict.StockEnd - start).ToList(), start, rows),
            conflict.Sides.Select(s => new ConflictSide(s.Mod, RowList(s.Units, start, rows))).ToList());
    }

    private static string RowList(IReadOnlyList<string> lines, int firstIndex, TableRowDescriber rows) =>
        lines.Count == 0 ? ConflictText.Removed : string.Join(" | ", lines.Select((l, k) => rows.Summary(firstIndex + k, l)));

    /// <summary>
    /// Merges non-blank rows keyed by their id column, keeping stock's blank lines in place and
    /// appending new rows at the end. Null when some copy repeats an id, which leaves the file
    /// to the line merge.
    /// </summary>
    private static (List<string> Lines, List<MergeConflict> Conflicts)? MergeRowsById(
        IReadOnlyList<string> stock, IReadOnlyList<(string Mod, TextLines Text)> texts, int column, TableRowDescriber? rows)
    {
        var stockRows = KeyRows(stock, column);
        var versionRows = texts.Select(t => new ModKeyedUnits(t.Mod, KeyRows(t.Text.Lines, column))).ToList();
        if (!UnitMerge.HasUniqueKeys(stockRows) || versionRows.Any(v => !UnitMerge.HasUniqueKeys(v.Units)))
            return null;

        var merged = UnitMerge.MergeKeyed(stockRows, versionRows);
        var conflicts = merged.Conflicts.Select(c => DescribeKeyedRow(c, rows)).ToList();

        var values = merged.Units.ToDictionary(u => u.Key, u => u.Value);
        var stockKeys = new HashSet<string>(stockRows.Select(r => r.Key));
        var lines = new List<string>();
        for (int i = 0; i < stock.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(stock[i])) lines.Add(stock[i]);
            else if (values.TryGetValue(RowKey(stock[i], column, i), out string? value)) lines.Add(value);
        }
        lines.AddRange(merged.Units.Where(u => !stockKeys.Contains(u.Key)).Select(u => u.Value));
        return (lines, conflicts);
    }

    private static MergeConflict DescribeKeyedRow(KeyConflict conflict, TableRowDescriber? rows)
    {
        if (rows is null || conflict.Stock is null)
            return ConflictText.FromKey("row " + conflict.Key, conflict, value => ConflictText.Join([value]));
        return new MergeConflict("row " + conflict.Key, "",
            conflict.Sides.Select(s => new ConflictSide(s.Mod,
                s.Value is null ? ConflictText.Removed : rows.FieldChanges(conflict.Stock, s.Value))).ToList());
    }

    private static List<KeyValuePair<string, string>> KeyRows(IReadOnlyList<string> lines, int column)
    {
        var rows = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < lines.Count; i++)
            if (!string.IsNullOrWhiteSpace(lines[i]))
                rows.Add(new(RowKey(lines[i], column, i), lines[i]));
        return rows;
    }

    /// <summary>
    /// "id 241" from the id column, or "line 12" for a row without one.
    /// </summary>
    private static string RowKey(string line, int column, int index)
    {
        var fields = CsvFields.Split(line);
        return fields.Count > column && fields[column].Trim().Length > 0
            ? "id " + fields[column].Trim()
            : $"line {index + 1}";
    }
}
