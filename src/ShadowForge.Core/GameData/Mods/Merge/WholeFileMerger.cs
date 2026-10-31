namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// For formats with no smaller unit to merge on: textures, models, motions, event scenes,
/// effects and other binaries. Two mods shipping different copies are changing the same object,
/// which is a conflict.
/// </summary>
internal sealed class WholeFileMerger : IFormatMerger
{
    public string Method => "whole file";

    public FileMergeResult Merge(string fileName, byte[] stock, IReadOnlyList<ModVersion> versions) =>
        FileMergeResult.Conflicted(Method,
        [
            new MergeConflict($"the whole {Kind(fileName)}", $"{stock.Length} bytes",
                versions.Select(v => new ConflictSide(v.Mod, $"its own copy, {v.Data.Length} bytes")).ToList()),
        ]);

    public static string Kind(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".dds" or ".36t" => "texture",
        ".hdb" => "model",
        ".hmb" => "motion",
        ".evt" => "event scene",
        ".eff" or ".efp" => "effect",
        ".hoc" or ".hocb" => "collision mesh",
        ".sfd" => "movie",
        ".xsca" => "camera",
        _ => "file",
    };
}
