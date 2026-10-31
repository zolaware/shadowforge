namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// One mod's copy of a file.
/// </summary>
internal sealed record ModVersion(string Mod, byte[] Data);

/// <summary>
/// One mod's version of a contested unit, rendered for a report.
/// </summary>
public sealed record ConflictSide(string Mod, string Value);

/// <summary>
/// A unit two or more mods change in different ways: a table row, script line, formation, cue,
/// wave, archive entry or a whole file. Location names the unit, Stock is its shipped value.
/// </summary>
public sealed record MergeConflict(string Location, string Stock, IReadOnlyList<ConflictSide> Sides);

/// <summary>
/// The outcome of merging the copies of one file. Data is set only when every mod's changes
/// were combined. Otherwise Conflicts lists the units the mods both change, or Problem says why
/// the file could not be compared unit by unit.
/// </summary>
public sealed record FileMergeResult(
    byte[]? Data, string Method, IReadOnlyList<MergeConflict> Conflicts, string? Problem)
{
    public bool IsMerged => Data is not null;

    public static FileMergeResult Merged(byte[] data, string method) => new(data, method, [], null);

    public static FileMergeResult Conflicted(string method, IReadOnlyList<MergeConflict> conflicts) =>
        new(null, method, conflicts, null);

    public static FileMergeResult Unmergeable(string method, string problem) => new(null, method, [], problem);
}

/// <summary>
/// Merges two or more mods' changed copies of one file against the shipped copy.
/// </summary>
internal interface IFormatMerger
{
    /// <summary>
    /// How this merger combines a file, for reports.
    /// </summary>
    string Method { get; }

    FileMergeResult Merge(string fileName, byte[] stock, IReadOnlyList<ModVersion> versions);
}
