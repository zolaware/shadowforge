namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Describes rows of a game table for conflict reports. A row is named by what it is where the
/// table is known (an item, an enemy, a character model), and a change is listed as the columns
/// it touches, as "HP 2250 -> 3000". Columns that hold item ids show the item. The enemy stat
/// tables (enemy_zako, enemy_boss) share one column layout, so their columns are named by
/// position; any other table uses the header row the file itself carries.
/// </summary>
internal sealed class TableRowDescriber
{
    private static readonly Dictionary<int, string> EnemyStatColumns = new()
    {
        [1] = "Category", [2] = "Size", [3] = "Grid", [4] = "Name", [5] = "Race", [6] = "Level",
        [7] = "HP", [8] = "MP", [9] = "Attack", [10] = "Magic attack", [11] = "Defense",
        [12] = "Magic defense", [13] = "Evasion", [14] = "Recovery", [15] = "Accuracy", [16] = "Speed",
        [18] = "Neutral resist", [19] = "Fire resist", [20] = "Water resist", [21] = "Wind resist",
        [22] = "Earth resist", [23] = "Light resist", [24] = "Shadow resist", [25] = "Sleep resist",
        [26] = "Kelolon resist", [28] = "Poison resist", [29] = "Paralysis resist", [30] = "Panic resist",
        [32] = "Petrify resist", [33] = "Faint resist", [34] = "Death resist", [43] = "Slow resist",
        [44] = "Stop resist", [45] = "EXP", [46] = "SP", [47] = "Gold", [48] = "Medals",
        [49] = "Drop item 1", [50] = "Drop rate 1", [51] = "Drop item 2", [52] = "Drop rate 2",
        [53] = "Drop item 3", [54] = "Drop rate 3", [55] = "Drop item 4", [56] = "Drop rate 4",
        [57] = "Movement type",
    };

    private static readonly HashSet<int> EnemyDropItemColumns = [49, 51, 53, 55];

    private readonly RowIdentity _identity;
    private readonly bool _enemyStats;
    private readonly GameNames _names;
    private readonly List<string> _headers;
    private readonly int _headerLines;

    private enum RowIdentity { Position, Item, ZakoEnemy, BossEnemy, ModelColumn }

    public TableRowDescriber(string fileName, IReadOnlyList<string> stockLines, GameNames names)
    {
        _names = names;
        string name = Path.GetFileName(fileName).ToLowerInvariant();
        _identity = name switch
        {
            _ when name.StartsWith("ittb") || name.StartsWith("itwd") => RowIdentity.Item,
            _ when name.StartsWith("enemy_zako") || name.StartsWith("pre_enemy_zako") => RowIdentity.ZakoEnemy,
            _ when name.StartsWith("enemy_boss") => RowIdentity.BossEnemy,
            _ when name.StartsWith("id_chr_model") || name.StartsWith("namelist_chr") => RowIdentity.ModelColumn,
            _ => RowIdentity.Position,
        };
        _enemyStats = name.StartsWith("enemy_zako") || name.StartsWith("enemy_boss");

        bool hasHeader = _identity != RowIdentity.Item && LooksLikeHeader(stockLines);
        _headerLines = hasHeader ? 1 : 0;
        _headers = hasHeader ? CsvFields.Split(stockLines[0]) : [];
    }

    /// <summary>
    /// True for tables: .u16 and .csv files.
    /// </summary>
    public static bool IsTable(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".u16" or ".csv";

    /// <summary>
    /// What the row at this 0-based line is: "item 241 &lt;name&gt;", "enemy em045 &lt;name&gt;",
    /// "model bs17 &lt;name&gt;", or "row 12 (pointID 3)" for a table this does not know.
    /// </summary>
    public string RowLabel(int lineIndex, string line)
    {
        var fields = CsvFields.Split(line);
        string first = fields[0].Trim();
        switch (_identity)
        {
            case RowIdentity.Item:
                return _names.ItemLabel(lineIndex + 1);
            case RowIdentity.ZakoEnemy or RowIdentity.BossEnemy when int.TryParse(first, out int no):
                string model = GameNames.EnemyModelId(_identity == RowIdentity.BossEnemy ? 1 : 0, no);
                string fallback = fields.Count > 4 ? fields[4].Trim() : "";
                return _names.Model(model) is { } english ? $"enemy {model} {english}" : $"enemy {model} {fallback}".TrimEnd();
            case RowIdentity.ModelColumn when fields.Count > 1 && fields[1].Trim().Length > 0:
                return "model " + _names.ModelLabel(fields[1].Trim());
        }

        int row = lineIndex - _headerLines + 1;
        if (first.Length == 0) return $"row {row}";
        return _headers.Count > 0 ? $"row {row} ({_headers[0].Trim()} {first})" : $"row {row} ({first})";
    }

    /// <summary>
    /// The columns a mod's row changes: "HP 2250 -> 3000, Drop item 1 50 &lt;name&gt; -> 241 &lt;name&gt;".
    /// </summary>
    public string FieldChanges(string stockLine, string modLine)
    {
        var before = CsvFields.Split(stockLine);
        var after = CsvFields.Split(modLine);
        var changes = new List<string>();
        for (int i = 0; i < Math.Max(before.Count, after.Count); i++)
        {
            string from = i < before.Count ? before[i].Trim() : "";
            string to = i < after.Count ? after[i].Trim() : "";
            if (from != to)
                changes.Add($"{ColumnName(i)} {Value(i, from)} -> {Value(i, to)}");
        }
        return changes.Count == 0 ? "only whitespace differs" : string.Join(", ", changes);
    }

    /// <summary>
    /// A new or removed row, by its label and leading fields.
    /// </summary>
    public string Summary(int lineIndex, string line) =>
        $"{RowLabel(lineIndex, line)}: {ConflictText.Join([line])}";

    private string ColumnName(int column)
    {
        if (_enemyStats && EnemyStatColumns.TryGetValue(column, out string? stat)) return stat;
        if (column >= _headers.Count || _headers[column].Trim().Length == 0) return $"column {column}";
        string header = _headers[column].Trim();
        return _headers.Count(h => h.Trim() == header) > 1 ? $"{header} (column {column})" : header;
    }

    /// <summary>
    /// A cell with the name of what it points at: an item id in an item column, a tagged item
    /// id (ITM185) or a model id (em045) anywhere.
    /// </summary>
    private string Value(int column, string value)
    {
        if (value.Length == 0) return "(blank)";
        bool itemColumn = (_enemyStats && EnemyDropItemColumns.Contains(column))
            || (column < _headers.Count && _headers[column].Contains("item", StringComparison.OrdinalIgnoreCase));
        string? name = itemColumn && int.TryParse(value, out int id) ? _names.Item(id)
            : value.StartsWith("ITM", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[3..], out int tagged) ? _names.Item(tagged)
            : _names.Model(value);
        return name is null ? value : $"{value} {name}";
    }

    /// <summary>
    /// A first line with no number in any field followed by a line with one is a header row.
    /// </summary>
    private static bool LooksLikeHeader(IReadOnlyList<string> lines)
    {
        if (lines.Count < 2) return false;
        static bool AnyNumber(string line) => CsvFields.Split(line).Any(f => double.TryParse(f.Trim(), out _));
        return !AnyNumber(lines[0]) && AnyNumber(lines[1]);
    }
}
