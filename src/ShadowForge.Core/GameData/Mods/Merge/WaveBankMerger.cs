using ShadowForge.Formats.XACT;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Merges XACT wave banks (.xwb) wave by wave, so two music or sound mods replacing different
/// tracks of one bank both keep theirs. Every copy must keep the shipped wave count and segment
/// layout, which a wave replacement does. The bank header, seek tables and entry names are
/// units of their own.
/// </summary>
internal sealed class WaveBankMerger : IFormatMerger
{
    private const string BankHeader = "bank header";
    private const string SeekTables = "seek tables";
    private const string EntryNames = "entry names";

    public string Method => "wave by wave";

    public FileMergeResult Merge(string fileName, byte[] stock, IReadOnlyList<ModVersion> versions)
    {
        var stockBank = WaveBank.Read(stock, fileName);
        var banks = new Dictionary<string, WaveBank>();
        foreach (var version in versions)
        {
            var bank = WaveBank.Read(version.Data, $"{version.Mod}'s {fileName}");
            if (bank.Entries.Count != stockBank.Entries.Count || !bank.SegmentOffsets.SequenceEqual(stockBank.SegmentOffsets))
                return FileMergeResult.Unmergeable(Method,
                    $"{version.Mod} changes the number of waves or the bank layout, so its waves cannot be lined up with the others");
            banks[version.Mod] = bank;
        }

        var stockUnits = ToUnits(stockBank);
        var modUnits = versions.Select(v => new ModKeyedUnits(v.Mod, ToUnits(banks[v.Mod]))).ToList();
        var merged = UnitMerge.MergeKeyed(stockUnits, modUnits);
        if (merged.Conflicts.Count > 0)
            return FileMergeResult.Conflicted(Method, merged.Conflicts
                .Select(c => new MergeConflict(c.Key, Describe(stockBank, c.Key),
                    c.Sides.Select(s => new ConflictSide(s.Mod, Describe(banks[s.Mod], c.Key))).ToList()))
                .ToList());

        var stockValues = stockUnits.ToDictionary(u => u.Key, u => u.Value);
        var modValues = modUnits.ToDictionary(m => m.Mod, m => m.Units.ToDictionary(u => u.Key, u => u.Value));
        var result = WaveBank.Read(stock, fileName);
        foreach (var (key, value) in merged.Units.Where(u => stockValues[u.Key] != u.Value))
        {
            string mod = modValues.First(m => m.Value[key] == value).Key;
            CopyUnit(banks[mod], result, key);
        }
        return FileMergeResult.Merged(result.Write(), Method);
    }

    private static List<KeyValuePair<string, string>> ToUnits(WaveBank bank)
    {
        var units = new List<KeyValuePair<string, string>>
        {
            new(BankHeader, ConflictText.Fingerprint(bank.BankData)),
            new(SeekTables, ConflictText.Fingerprint(bank.SeekTables)),
            new(EntryNames, ConflictText.Fingerprint(bank.EntryNames)),
        };
        for (int i = 0; i < bank.Entries.Count; i++)
        {
            var e = bank.Entries[i];
            units.Add(new($"wave {i}",
                $"{e.Flags}:{e.DurationSamples}:{e.Format.Packed}:{e.LoopRegionStartSample}:" +
                $"{e.LoopRegionTotalSamples}:{ConflictText.Fingerprint(e.Data)}"));
        }
        return units;
    }

    private static void CopyUnit(WaveBank source, WaveBank target, string key)
    {
        switch (key)
        {
            case BankHeader: target.BankData = source.BankData; break;
            case SeekTables: target.SeekTables = source.SeekTables; break;
            case EntryNames: target.EntryNames = source.EntryNames; break;
            default:
                int index = int.Parse(key["wave ".Length..]);
                target.Entries[index] = source.Entries[index];
                break;
        }
    }

    private static string Describe(WaveBank bank, string key)
    {
        if (!key.StartsWith("wave ", StringComparison.Ordinal)) return $"{key} of {bank.Name}";
        var entry = bank.Entries[int.Parse(key["wave ".Length..])];
        double seconds = entry.Format.SamplesPerSecond > 0 ? (double)entry.DurationSamples / entry.Format.SamplesPerSecond : 0;
        return $"{entry.Format.Tag} {entry.Format.Channels}ch {entry.Format.SamplesPerSecond} Hz, {seconds:0.0} s";
    }
}
