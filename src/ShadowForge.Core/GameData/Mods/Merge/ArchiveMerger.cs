using ShadowForge.Formats.IPK;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Merges IPK archives (.ipk, and the .mpk motion packs built on them) entry by entry. An entry
/// two mods both change is merged with the merger for its own format, so a table inside an
/// archive still merges row by row and a motion pack merges clip by clip. The rebuilt archive is
/// stored uncompressed, as the archive writer stores every archive.
/// </summary>
internal sealed class ArchiveMerger(FormatMergers formats) : IFormatMerger
{
    public string Method => "entry by entry";

    public FileMergeResult Merge(string fileName, byte[] stock, IReadOnlyList<ModVersion> versions)
    {
        var (alignment, stockEntries) = ReadEntries(stock);
        var entries = versions.ToDictionary(v => v.Mod, v => ReadEntries(v.Data).Entries);

        var contents = new Dictionary<string, byte[]>();
        var merged = UnitMerge.MergeKeyed(ToUnits(stockEntries, contents),
            versions.Select(v => new ModKeyedUnits(v.Mod, ToUnits(entries[v.Mod], contents))).ToList());

        var resolved = new Dictionary<string, byte[]>();
        var conflicts = new List<MergeConflict>();
        foreach (var conflict in merged.Conflicts)
        {
            string name = conflict.Key;
            if (conflict.Stock is null || conflict.Sides.Any(s => s.Value is null))
            {
                conflicts.Add(new MergeConflict("entry " + name,
                    conflict.Stock is null ? ConflictText.NotInStock : $"{stockEntries[name].Length} bytes",
                    conflict.Sides.Select(s => new ConflictSide(s.Mod,
                        s.Value is null ? ConflictText.Removed : $"{entries[s.Mod][name].Length} bytes")).ToList()));
                continue;
            }

            var inner = formats.Merge(name, stockEntries[name],
                conflict.Sides.Select(s => new ModVersion(s.Mod, entries[s.Mod][name])).ToList());
            if (inner.Data is { } data)
                resolved[name] = data;
            else if (inner.Problem is { } problem)
                return FileMergeResult.Unmergeable(Method, $"{name}: {problem}");
            else
                conflicts.AddRange(inner.Conflicts.Select(c => c with { Location = $"{name}: {c.Location}" }));
        }
        if (conflicts.Count > 0) return FileMergeResult.Conflicted(Method, conflicts);

        var output = merged.Units
            .Select(u => new ArchiveWriter.InputEntry(u.Key, resolved.GetValueOrDefault(u.Key) ?? contents[u.Value]))
            .ToList();
        return FileMergeResult.Merged(ArchiveWriter.Build(output, alignment), Method);
    }

    /// <summary>
    /// The archive's alignment and its entries by name, in archive order.
    /// </summary>
    private static (uint Alignment, Dictionary<string, byte[]> Entries) ReadEntries(byte[] data)
    {
        var (archive, entries) = ArchiveReader.ReadAllEntries(data);
        return (archive.Alignment, entries.ToDictionary(e => e.Name, e => e.Data));
    }

    /// <summary>
    /// One unit per entry, valued by its content fingerprint. Each entry's bytes are recorded in
    /// contents under that fingerprint to rebuild the archive from merged units.
    /// </summary>
    private static List<KeyValuePair<string, string>> ToUnits(Dictionary<string, byte[]> entries, Dictionary<string, byte[]> contents)
    {
        var units = new List<KeyValuePair<string, string>>(entries.Count);
        foreach (var (name, data) in entries)
        {
            string fingerprint = ConflictText.Fingerprint(data);
            contents.TryAdd(fingerprint, data);
            units.Add(new(name, fingerprint));
        }
        return units;
    }
}
