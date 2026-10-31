using ShadowForge.Formats.BDSL;
using ShadowForge.Formats.RPJ;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Merges scene scripts (.rpj) by decompiling every copy to BDSL, merging the text line by line
/// and compiling the result. Script mods work by inserting statements, so two mods that insert
/// in different places both keep their changes. Two mods changing the same statement conflict,
/// and the report shows the statements with item names beside their item ids.
/// </summary>
internal sealed class SceneScriptMerger(GameNames names) : IFormatMerger
{
    public string Method => "decompiled to BDSL, merged line by line, recompiled";

    public FileMergeResult Merge(string fileName, byte[] stock, IReadOnlyList<ModVersion> versions)
    {
        var stockLines = Decompile(stock);
        var merged = UnitMerge.MergeSequences(stockLines,
            versions.Select(v => new ModUnits(v.Mod, Decompile(v.Data))).ToList());
        if (merged.Conflicts.Count > 0)
            return FileMergeResult.Conflicted(Method, merged.Conflicts
                .Select(c => ConflictText.FromRange("script line", c, stockLines,
                    lines => names.AnnotateItemIds(ConflictText.Join(lines))))
                .ToList());

        return FileMergeResult.Merged(Compile(merged.Units), Method);
    }

    private static List<string> Decompile(byte[] rpj) =>
        TextScene.Write(BinaryScene.Read(rpj)).Split('\n').ToList();

    private static byte[] Compile(IReadOnlyList<string> lines)
    {
        try
        {
            return BinaryScene.Write(TextScene.Read(string.Join('\n', lines)));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"the merged script does not compile: {ex.Message}", ex);
        }
    }
}
