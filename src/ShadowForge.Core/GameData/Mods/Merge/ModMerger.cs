namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// A file one mod alone ships, copied into the merged mod unchanged.
/// </summary>
public sealed record OwnFile(string VfsPath, string Mod, string SourcePath);

/// <summary>
/// A file several mods ship, with how their copies merge. Subject names what the file is about
/// when its name says (an enemy's AI or motion script), and is null otherwise.
/// </summary>
public sealed record SharedFile(string VfsPath, IReadOnlyList<string> Mods, string? Subject, FileMergeResult Result);

/// <summary>
/// A file mods ship that an enabled DLC package also ships, so the game never reads the mods' copy.
/// </summary>
public sealed record ShadowedFile(string VfsPath, IReadOnlyList<string> Mods, IReadOnlyList<string> Packages);

/// <summary>
/// Everything a merge would do, worked out without writing anything. It can be written only when
/// every shared file merged.
/// </summary>
public sealed record MergePlan(
    IReadOnlyList<string> Mods,
    IReadOnlyList<OwnFile> OwnFiles,
    IReadOnlyList<SharedFile> SharedFiles,
    IReadOnlyList<ShadowedFile> Shadowed,
    IReadOnlyList<string> EnabledDlc,
    IReadOnlyList<PatchedPackage> PatchedDlc)
{
    public bool CanWrite => SharedFiles.All(f => f.Result.IsMerged);

    public IEnumerable<SharedFile> Blocked => SharedFiles.Where(f => !f.Result.IsMerged);
}

/// <summary>
/// Combines installed mods into one. A file one mod ships is copied, a file several ship is
/// merged against the game's own copy unit by unit, and the merged mod is written only when no
/// two mods change the same unit in different ways.
/// </summary>
public sealed class ModMerger(GameInstall install, string? profile = null)
{
    public const string ReadmeName = "readme.txt";

    public string ModsRoot => install.ModsRoot
        ?? throw new InvalidOperationException(
            "The located root is a loose extract, not a reblue install - there are no mods to merge.");

    public MergePlan Plan(IReadOnlyList<string> modNames)
    {
        if (modNames.Count < 2)
            throw new ArgumentException("Name at least two mods to merge.", nameof(modNames));
        if (modNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != modNames.Count)
            throw new ArgumentException("A mod is named more than once.", nameof(modNames));

        var owners = new Dictionary<string, List<(string Mod, string Path)>>(StringComparer.OrdinalIgnoreCase);
        foreach (string mod in modNames)
        {
            string dir = Path.Combine(ModsRoot, ModPath.Segment(mod, "a mod name"));
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"No mod named '{mod}' in {ModsRoot}");
            foreach (var (vfs, path) in ModFiles(dir))
            {
                if (!owners.TryGetValue(vfs, out var list)) owners[vfs] = list = [];
                list.Add((mod, path));
            }
        }

        var stock = new StockFiles(install);
        var names = GameNames.FromTables(
            NameTables(GameNames.ItemNamesPath, stock, owners), NameTables(GameNames.CharacterNamesPath, stock, owners));
        var formats = new FormatMergers(names);
        var dlc = DlcPackages.Load(install, profile);

        var own = new List<OwnFile>();
        var shared = new List<SharedFile>();
        var shadowed = new List<ShadowedFile>();
        foreach (var (vfs, files) in owners.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase))
        {
            var mods = files.Select(f => f.Mod).ToList();
            if (dlc.Shadowing(vfs) is { Count: > 0 } packages)
                shadowed.Add(new ShadowedFile(vfs, mods, packages));

            if (files.Count == 1)
            {
                own.Add(new OwnFile(vfs, files[0].Mod, files[0].Path));
                continue;
            }
            var versions = files.Select(f => new ModVersion(f.Mod, File.ReadAllBytes(f.Path))).ToList();
            var result = formats.Merge(Path.GetFileName(vfs), stock.Read(vfs), versions);
            shared.Add(new SharedFile(vfs, mods, names.SubjectOf(vfs), result));
        }
        return new MergePlan(modNames, own, shared, shadowed, dlc.Enabled, dlc.Patched);
    }

    /// <summary>
    /// Writes the merged mod into modDir with its mod.toml and a readme of the plan. Returns the
    /// number of game files written.
    /// </summary>
    public static int Write(MergePlan plan, string modDir, string modName)
    {
        if (!plan.CanWrite)
            throw new InvalidOperationException(
                $"{plan.Blocked.Count()} file(s) cannot be merged, so nothing was written.");

        foreach (var file in plan.OwnFiles)
        {
            string dest = VfsPath.Under(modDir, file.VfsPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file.SourcePath, dest, overwrite: true);
        }
        foreach (var file in plan.SharedFiles)
        {
            string dest = VfsPath.Under(modDir, file.VfsPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, file.Result.Data!);
        }

        ModDeployer.WriteToml(Path.Combine(modDir, "mod.toml"), new ModMetadata(
            modName, "merged", "1.0.0", $"{string.Join(", ", plan.Mods)}, merged into one mod."));
        File.WriteAllText(Path.Combine(modDir, ReadmeName),
            string.Join("\r\n", MergeReport.Lines(plan, modName, conflictsPerFile: int.MaxValue)) + "\r\n");
        return plan.OwnFiles.Count + plan.SharedFiles.Count;
    }

    /// <summary>
    /// The shipped copy of a name table followed by every merged mod's copy.
    /// </summary>
    private static IEnumerable<byte[]> NameTables(
        string vfsPath, StockFiles stock, Dictionary<string, List<(string Mod, string Path)>> owners)
    {
        if (stock.Read(vfsPath) is { } shipped) yield return shipped;
        foreach (var (_, path) in owners.GetValueOrDefault(vfsPath) ?? [])
            yield return File.ReadAllBytes(path);
    }

    /// <summary>
    /// A mod's files by the game path each overrides. Files at the mod's root (mod.toml, readme
    /// and the like) override nothing and are left out.
    /// </summary>
    public static IEnumerable<(string VfsPath, string Path)> ModFiles(string modDir) =>
        Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories)
            .Select(path => (VfsPath.Normalize(Path.GetRelativePath(modDir, path)), path))
            .Where(f => f.Item1.Contains('\\'));
}
