namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Picks the merger for a file by its extension and runs the steps every merge shares: copies
/// equal to stock change nothing, a single distinct change needs no merging, and only two or more
/// different changes reach a format merger.
/// </summary>
internal sealed class FormatMergers
{
    private readonly TextMerger _text;
    private readonly SceneScriptMerger _sceneScript;
    private readonly EnemyPlacementMerger _enemyPlacement;
    private readonly WaveBankMerger _waveBank = new();
    private readonly SoundBankMerger _soundBank = new();
    private readonly ArchiveMerger _archive;
    private readonly WholeFileMerger _wholeFile = new();

    /// <summary>
    /// names labels items and enemies in conflict reports; GameNames.Empty leaves ids bare.
    /// </summary>
    public FormatMergers(GameNames names)
    {
        _text = new TextMerger(names);
        _sceneScript = new SceneScriptMerger(names);
        _enemyPlacement = new EnemyPlacementMerger(names);
        _archive = new ArchiveMerger(this);
    }

    public IFormatMerger For(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".rpj" => _sceneScript,
        ".etl" => _enemyPlacement,
        ".xwb" => _waveBank,
        ".xsb" => _soundBank,
        ".ipk" or ".mpk" => _archive,
        _ when TextMerger.Handles(fileName) => _text,
        _ => _wholeFile,
    };

    /// <summary>
    /// Merges every mod's copy of one file against stock, which is null when the game does not
    /// ship the file. A merger that cannot read a copy reports why instead of throwing.
    /// </summary>
    public FileMergeResult Merge(string fileName, byte[]? stock, IReadOnlyList<ModVersion> versions)
    {
        var changed = stock is null ? versions : versions.Where(v => !v.Data.AsSpan().SequenceEqual(stock)).ToList();
        if (changed.Count == 0)
            return FileMergeResult.Merged(stock!, "no mod changes it");
        if (changed.All(v => v.Data.AsSpan().SequenceEqual(changed[0].Data)))
            return FileMergeResult.Merged(changed[0].Data,
                changed.Count == 1 ? $"only {changed[0].Mod} changes it" : "every mod makes the same change");
        if (stock is null)
            return FileMergeResult.Conflicted("whole file",
            [
                new MergeConflict($"the whole {WholeFileMerger.Kind(fileName)}", ConflictText.NotInStock,
                    changed.Select(v => new ConflictSide(v.Mod, $"adds its own copy, {v.Data.Length} bytes")).ToList()),
            ]);

        var merger = For(fileName);
        try
        {
            return merger.Merge(fileName, stock, changed);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException
                                       or IndexOutOfRangeException or EndOfStreamException)
        {
            return FileMergeResult.Unmergeable(merger.Method, ex.Message);
        }
    }
}
