using ShadowForge.Formats.XACT;
using ShadowForge.IO;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Merges XACT sound banks (.xsb) cue by cue, for mods that change which wave a cue plays, as
/// a music randomizer does. A copy that changes anything in the bank besides those wave
/// references cannot be merged this way.
/// </summary>
internal sealed class SoundBankMerger : IFormatMerger
{
    public string Method => "cue by cue";

    public FileMergeResult Merge(string fileName, byte[] stock, IReadOnlyList<ModVersion> versions)
    {
        var bank = SoundBank.Read(stock, fileName);
        var references = new bool[stock.Length];
        foreach (var cue in bank.Cues)
            Array.Fill(references, true, cue.WaveReferenceOffset, SoundBank.WaveReferenceSize);

        foreach (var version in versions)
            if (version.Data.Length != stock.Length || !SameOutside(references, stock, version.Data))
                return FileMergeResult.Unmergeable(Method, $"{version.Mod} changes more than which wave each cue plays");

        var merged = UnitMerge.MergeKeyed(ToUnits(bank, stock),
            versions.Select(v => new ModKeyedUnits(v.Mod, ToUnits(bank, v.Data))).ToList());
        if (merged.Conflicts.Count > 0)
            return FileMergeResult.Conflicted(Method, merged.Conflicts
                .Select(c => ConflictText.FromKey(
                    "cue " + bank.Cues[int.Parse(c.Key)].Name, c, value => Describe(bank, value)))
                .ToList());

        var result = stock.ToArray();
        foreach (var (key, value) in merged.Units)
        {
            var cue = bank.Cues[int.Parse(key)];
            Convert.FromHexString(value).CopyTo(result, cue.WaveReferenceOffset);
        }
        return FileMergeResult.Merged(result, Method);
    }

    /// <summary>
    /// One unit per cue, keyed by cue index: the hex of its wave reference bytes in this copy.
    /// </summary>
    private static List<KeyValuePair<string, string>> ToUnits(SoundBank bank, byte[] data) =>
        bank.Cues
            .Select(c => new KeyValuePair<string, string>(c.Index.ToString(),
                Convert.ToHexString(data, c.WaveReferenceOffset, SoundBank.WaveReferenceSize)))
            .ToList();

    private static bool SameOutside(bool[] references, byte[] stock, byte[] copy)
    {
        for (int i = 0; i < stock.Length; i++)
            if (!references[i] && stock[i] != copy[i]) return false;
        return true;
    }

    /// <summary>
    /// "wave 12 of BG_01" from a cue's wave reference bytes.
    /// </summary>
    private static string Describe(SoundBank bank, string hex)
    {
        byte[] reference = Convert.FromHexString(hex);
        int wave = BigEndian.ReadUInt16(reference, 0);
        int waveBank = reference[2];
        string bankName = waveBank < bank.WaveBankNames.Count ? bank.WaveBankNames[waveBank] : $"wave bank {waveBank}";
        return $"wave {wave} of {bankName}";
    }
}
