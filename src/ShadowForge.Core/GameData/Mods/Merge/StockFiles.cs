using ShadowForge.Formats.IPK;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Finds the shipped copy of a file a mod overrides. The game path is tried first. A localised
/// table is requested under the pack that uses it but ships in another (dg05_it01_us.u16 is
/// asked for under script\dungeon and lives in packmem_us.ipk), so a miss falls back to the
/// archive entry with the same file name whose path shares the most trailing folders. Paths
/// under download\ are served from the installed DLC packages, preferring the untouched .orig
/// copy a package patch leaves beside them.
/// </summary>
internal sealed class StockFiles(GameInstall install)
{
    private const string DownloadPrefix = @"download\";

    private readonly GameFileSystem _files = new(install);
    private Dictionary<string, List<PackedFile>>? _gameEntries;
    private Dictionary<string, List<PackedFile>>? _dlcEntries;

    private sealed record PackedFile(string ArchivePath, FileEntry Entry, bool UsesZlib);

    /// <summary>
    /// The shipped bytes, or null when the game ships no such file.
    /// </summary>
    public byte[]? Read(string vfsPath)
    {
        string vfs = VfsPath.Normalize(vfsPath);
        if (vfs.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
            return ReadByFileName(vfs, _dlcEntries ??= IndexArchives(DlcArchives()));

        try { return _files.ReadVfs(vfs); }
        catch (IOException) { }
        return ReadByFileName(vfs, _gameEntries ??= IndexArchives(GameArchives()));
    }

    private static byte[]? ReadByFileName(string vfs, Dictionary<string, List<PackedFile>> index)
    {
        if (!index.TryGetValue(Path.GetFileName(vfs), out var candidates)) return null;
        var best = candidates.MaxBy(c => SharedTrailingFolders(vfs, c.Entry.Name))!;
        using var stream = File.OpenRead(best.ArchivePath);
        using var output = new MemoryStream();
        ArchiveReader.ExtractEntry(stream, best.Entry, output, best.UsesZlib);
        return output.ToArray();
    }

    private static int SharedTrailingFolders(string vfs, string entryName)
    {
        string[] a = vfs.Split('\\'), b = entryName.Replace('/', '\\').Split('\\');
        int shared = 0;
        while (shared < a.Length && shared < b.Length
               && a[^(shared + 1)].Equals(b[^(shared + 1)], StringComparison.OrdinalIgnoreCase))
            shared++;
        return shared;
    }

    private static Dictionary<string, List<PackedFile>> IndexArchives(IEnumerable<string> archives)
    {
        var index = new Dictionary<string, List<PackedFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in archives)
        {
            Archive archive;
            using (var stream = File.OpenRead(path))
            {
                try { archive = ArchiveReader.ReadArchive(stream); }
                catch (InvalidDataException) { continue; }
            }
            foreach (var entry in archive.Entries)
            {
                string name = Path.GetFileName(entry.Name.Replace('\\', '/'));
                if (!index.TryGetValue(name, out var list)) index[name] = list = [];
                list.Add(new PackedFile(path, entry, archive.UsesZlib));
            }
        }
        return index;
    }

    private IEnumerable<string> GameArchives()
    {
        string pack = Path.Combine(install.GameDataRoot, "pack");
        return Directory.Exists(pack)
            ? Directory.EnumerateFiles(pack, "*.ipk", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
            : [];
    }

    private IEnumerable<string> DlcArchives() =>
        DlcPackages.PackagePaths(install)
            .Select(p => File.Exists(p + DlcPackages.BackupSuffix) ? p + DlcPackages.BackupSuffix : p);
}
