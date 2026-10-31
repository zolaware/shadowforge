using ShadowForge.Formats.IPK;
using Tomlyn;
using Tomlyn.Model;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// A DLC package edited in place: entries that differ from, or are missing in, its .orig backup.
/// </summary>
public sealed record PatchedPackage(string Package, string ArchivePath, IReadOnlyList<string> ChangedEntries);

/// <summary>
/// The item DLC packages of an install, each a Download\DL_Param.ipk under dlc\{package} and
/// again inside the profile. With a package enabled the game asks for its tables under the
/// download namespace, which takes no mod overrides, so a mod shipping a file the package also
/// ships has no effect while the package is on. Enabled packages are listed in the profile's
/// dlc_state.toml.
/// </summary>
internal sealed class DlcPackages
{
    public const string BackupSuffix = ".orig";

    private readonly Dictionary<string, List<string>> _packagesByFileName;

    private DlcPackages(IReadOnlyList<string> enabled, Dictionary<string, List<string>> packagesByFileName,
        IReadOnlyList<PatchedPackage> patched)
    {
        Enabled = enabled;
        _packagesByFileName = packagesByFileName;
        Patched = patched;
    }

    public IReadOnlyList<string> Enabled { get; }

    public IReadOnlyList<PatchedPackage> Patched { get; }

    public static DlcPackages Load(GameInstall install, string? profile)
    {
        var enabled = EnabledPackages(install, profile);
        var byFileName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string package in enabled)
        {
            string archive = Path.Combine(install.InstallRoot ?? "", "dlc", package, "Download", "DL_Param.ipk");
            if (!File.Exists(archive)) continue;
            foreach (string name in EntryNames(archive))
            {
                string fileName = Path.GetFileName(name.Replace('\\', '/'));
                if (!byFileName.TryGetValue(fileName, out var list)) byFileName[fileName] = list = [];
                list.Add(package);
            }
        }
        return new DlcPackages(enabled, byFileName, PatchedPackages(install));
    }

    /// <summary>
    /// The enabled packages that ship a file of this name, whose copy the game reads instead.
    /// </summary>
    public IReadOnlyList<string> Shadowing(string vfsPath) =>
        _packagesByFileName.GetValueOrDefault(Path.GetFileName(vfsPath.Replace('\\', '/'))) ?? [];

    /// <summary>
    /// Every DL_Param.ipk of the install, under dlc\ and inside the profiles.
    /// </summary>
    public static IEnumerable<string> PackagePaths(GameInstall install)
    {
        if (install.InstallRoot is null) return [];
        return new[] { "dlc", "profiles" }
            .Select(d => Path.Combine(install.InstallRoot, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "DL_Param.ipk", SearchOption.AllDirectories))
            .Order(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> EnabledPackages(GameInstall install, string? profile)
    {
        string state = Path.Combine(install.InstallRoot ?? "", "profiles", profile ?? "default", "dlc_state.toml");
        if (install.InstallRoot is null || !File.Exists(state)) return [];
        var doc = Toml.ToModel(File.ReadAllText(state));
        if (!doc.TryGetValue("dlc", out var value) || value is not TomlTableArray packages) return [];
        return packages
            .Where(p => p.TryGetValue("enabled", out var on) && on is true)
            .Select(p => p.TryGetValue("file_name", out var name) ? name as string : null)
            .OfType<string>()
            .ToList();
    }

    private static List<PatchedPackage> PatchedPackages(GameInstall install)
    {
        var patched = new List<PatchedPackage>();
        foreach (string archive in PackagePaths(install))
        {
            string backup = archive + BackupSuffix;
            if (!File.Exists(backup)) continue;
            byte[] live = File.ReadAllBytes(archive), original = File.ReadAllBytes(backup);
            if (live.AsSpan().SequenceEqual(original)) continue;

            var before = ArchiveReader.ReadAllEntries(original).Entries
                .ToDictionary(e => e.Name, e => e.Data, StringComparer.OrdinalIgnoreCase);
            var changed = ArchiveReader.ReadAllEntries(live).Entries
                .Where(e => !before.TryGetValue(e.Name, out var old) || !old.AsSpan().SequenceEqual(e.Data))
                .Select(e => e.Name)
                .ToList();
            string package = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(archive))!);
            patched.Add(new PatchedPackage(package, archive, changed));
        }
        return patched;
    }

    private static IEnumerable<string> EntryNames(string archive)
    {
        using var stream = File.OpenRead(archive);
        return ArchiveReader.ReadArchive(stream).Entries.Select(e => e.Name).ToList();
    }
}
