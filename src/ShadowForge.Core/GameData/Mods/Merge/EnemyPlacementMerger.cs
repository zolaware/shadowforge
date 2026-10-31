using ShadowForge.Formats.ETL;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Merges enemy placement tables (.etl) formation by formation. Mods changing different
/// formations of a map combine. Two mods placing different enemies in the same formation
/// conflict, and the report names the enemies each mod puts there.
/// </summary>
internal sealed class EnemyPlacementMerger(GameNames names) : IFormatMerger
{
    public string Method => "formation by formation";

    public FileMergeResult Merge(string fileName, byte[] stock, IReadOnlyList<ModVersion> versions)
    {
        var formations = new Dictionary<string, Formation>();
        var stockUnits = ToUnits(EnemyPlacement.Read(stock), formations);
        var versionUnits = versions
            .Select(v => new ModUnits(v.Mod, ToUnits(EnemyPlacement.Read(v.Data), formations)))
            .ToList();

        var merged = UnitMerge.MergeSequences(stockUnits, versionUnits);
        if (merged.Conflicts.Count > 0)
            return FileMergeResult.Conflicted(Method, merged.Conflicts
                .Select(c => ConflictText.FromRange("formation", c, stockUnits,
                    units => string.Join(" / ", units.Select(u => Describe(formations[u])))))
                .ToList());

        return FileMergeResult.Merged(EnemyPlacement.Write(merged.Units.Select(u => formations[u]).ToList()), Method);
    }

    /// <summary>
    /// One string per formation holding every field, so equal strings mean equal formations.
    /// Each is recorded in formations to rebuild the table from merged units.
    /// </summary>
    private static List<string> ToUnits(List<Formation> table, Dictionary<string, Formation> formations)
    {
        var units = new List<string>(table.Count);
        foreach (var formation in table)
        {
            string unit = formation.Weight + ":" +
                string.Join(",", formation.Spawns.Select(s => $"{s.Enemy:X8}.{s.Param}.{s.Slot}"));
            formations.TryAdd(unit, formation);
            units.Add(unit);
        }
        return units;
    }

    /// <summary>
    /// "zako 45 em045 &lt;name&gt; x2, boss 17 bs17 &lt;name&gt;", counting repeats of one enemy.
    /// </summary>
    private string Describe(Formation formation)
    {
        if (formation.Spawns.Count == 0) return "(no enemies)";
        return string.Join(", ", formation.Spawns
            .GroupBy(s => s.Enemy)
            .Select(g =>
            {
                var spawn = g.First();
                string label = spawn.Table is 0 or 1
                    ? $"{spawn} {names.ModelLabel(GameNames.EnemyModelId(spawn.Table, spawn.Row))}"
                    : spawn.ToString();
                return g.Count() > 1 ? $"{label} x{g.Count()}" : label;
            }));
    }
}
