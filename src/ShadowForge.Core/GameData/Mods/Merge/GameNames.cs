using System.Text.RegularExpressions;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// English names for the ids a merge report mentions. Items come from ItWd_us.u16, where item
/// id N is row N - 1. Characters and enemies come from namelist_chr_us.u16, keyed by model id
/// (em045, bs17, pc01). Enemy table row N is model emNNN in enemy_zako and bsNN in enemy_boss.
/// </summary>
internal sealed partial class GameNames
{
    public const string ItemNamesPath = @"database\ItWd_us.u16";
    public const string CharacterNamesPath = @"!necessity\bd_gamedat\namelist_chr_us.u16";

    private readonly List<string> _items;
    private readonly Dictionary<string, string> _models;

    private GameNames(List<string> items, Dictionary<string, string> models)
    {
        _items = items;
        _models = models;
    }

    public static GameNames Empty { get; } = new([], []);

    /// <summary>
    /// Builds the names from copies of ItWd_us.u16 and namelist_chr_us.u16, the shipped copy
    /// first. A later copy fills only the names the earlier ones lack, so items and enemies a
    /// mod adds are named from that mod's own tables.
    /// </summary>
    public static GameNames FromTables(IEnumerable<byte[]> itemNameTables, IEnumerable<byte[]> characterNameTables)
    {
        var items = new List<string>();
        foreach (var table in itemNameTables)
        {
            var rows = TableLines(table).Select(l => CsvFields.Split(l)[0].Trim()).ToList();
            for (int i = 0; i < rows.Count; i++)
            {
                if (i == items.Count) items.Add(rows[i]);
                else if (items[i].Length == 0) items[i] = rows[i];
            }
        }

        var models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in characterNameTables)
            foreach (string line in TableLines(table))
                if (CsvFields.Split(line) is [_, var id, var name, ..] && id.Trim().Length > 0 && name.Trim().Length > 0)
                    models.TryAdd(id.Trim(), name.Trim());

        return new GameNames(items, models);
    }

    public string? Item(int id) =>
        id >= 1 && id <= _items.Count && _items[id - 1].Length > 0 ? _items[id - 1] : null;

    public string? Model(string modelId) => _models.GetValueOrDefault(modelId);

    /// <summary>
    /// The model id of a row of enemy_zako (table 0) or enemy_boss (table 1).
    /// </summary>
    public static string EnemyModelId(int table, int row) => table == 1 ? $"bs{row:00}" : $"em{row:000}";

    /// <summary>
    /// "item 241 &lt;name&gt;", or "item 241" when the name is unknown.
    /// </summary>
    public string ItemLabel(int id) => Item(id) is { } name ? $"item {id} {name}" : $"item {id}";

    /// <summary>
    /// "em045 &lt;name&gt;", or the bare id when the name is unknown.
    /// </summary>
    public string ModelLabel(string modelId) => Model(modelId) is { } name ? $"{modelId} {name}" : modelId;

    /// <summary>
    /// Adds the item name after every item=N in a line of script.
    /// </summary>
    public string AnnotateItemIds(string text) => ItemArgument().Replace(text, m =>
        int.TryParse(m.Groups[1].Value, out int id) && Item(id) is { } name ? $"{m.Value} ({name})" : m.Value);

    /// <summary>
    /// What a file is about when its name carries a model id, as motscr_em045.csv, aix_bs17.csv
    /// and model_em045.mdl do. Null otherwise.
    /// </summary>
    public string? SubjectOf(string fileName)
    {
        var match = ModelIdInName().Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success ? ModelLabel(match.Groups[1].Value.ToLowerInvariant()) : null;
    }

    private static IEnumerable<string> TableLines(byte[] data) =>
        TextLines.Decode(data, TextEncodingKind.UTF16BigEndian)?.Lines ?? [];

    [GeneratedRegex(@"\bitem=(\d+)")]
    private static partial Regex ItemArgument();

    [GeneratedRegex(@"(?:^|_)((?:em|bs|pc|np|sw)\d{2,3})(?:_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ModelIdInName();
}
