using ShadowForge.GameData;
using ShadowForge.GameData.Mods.Merge;

namespace ShadowForge.Tests.GameData.Mods.Merge;

public sealed class ModMergerTests
{
    private const string Table = @"database\t.u16";

    /// <summary>
    /// A temp install whose game ships database\t.u16 loose, with mods A and B beside it.
    /// </summary>
    private static (GameInstall Install, string Root) NewInstall(string stockTable, string tableA, string tableB)
    {
        string root = GameDataFixture.NewTempDir("sf_merge_");
        Write(root, @"game\" + Table, TextMergerTests.UTF16(stockTable));
        Write(root, @"mods\A\" + Table, TextMergerTests.UTF16(tableA));
        Write(root, @"mods\B\" + Table, TextMergerTests.UTF16(tableB));
        Write(root, @"mods\A\mod.toml", "[mod]\nname = \"A\"\n"u8.ToArray());
        Write(root, @"mods\A\script\town\only_a.csv", "a\r\n"u8.ToArray());
        return (GameInstall.Locate(Path.Combine(root, "game")), root);
    }

    private static void Write(string root, string relative, byte[] data)
    {
        string path = Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    [Fact]
    public void Plan_ModsChangingDifferentRows_CanBeWritten()
    {
        var (install, root) = NewInstall("1,a\r\n2,b\r\n", "1,A\r\n2,b\r\n", "1,a\r\n2,B\r\n");

        var plan = new ModMerger(install).Plan(["A", "B"]);
        string modDir = Path.Combine(root, "out", "Merged");
        int written = ModMerger.Write(plan, modDir, "Merged");

        Assert.True(plan.CanWrite);
        Assert.Equal(2, written);
        Assert.Equal(TextMergerTests.UTF16("1,A\r\n2,B\r\n"), File.ReadAllBytes(Path.Combine(modDir, "database", "t.u16")));
        Assert.True(File.Exists(Path.Combine(modDir, "script", "town", "only_a.csv")));
        Assert.Contains("name = \"Merged\"", File.ReadAllText(Path.Combine(modDir, "mod.toml")));
        Assert.Contains("these mods can be merged", File.ReadAllText(Path.Combine(modDir, ModMerger.ReadmeName)));
    }

    [Fact]
    public void Plan_FilesAtTheModRoot_AreLeftOut()
    {
        var (install, _) = NewInstall("1,a\r\n", "1,a\r\n", "1,a\r\n");

        var plan = new ModMerger(install).Plan(["A", "B"]);

        Assert.DoesNotContain(plan.OwnFiles, f => f.VfsPath.Equals("mod.toml", StringComparison.OrdinalIgnoreCase));
        Assert.Equal([@"script\town\only_a.csv"], plan.OwnFiles.Select(f => f.VfsPath));
    }

    [Fact]
    public void Plan_RowChangedByBothMods_BlocksTheWrite()
    {
        var (install, root) = NewInstall("1,a\r\n", "1,x\r\n", "1,y\r\n");

        var plan = new ModMerger(install).Plan(["A", "B"]);

        Assert.False(plan.CanWrite);
        var blocked = Assert.Single(plan.Blocked);
        Assert.Equal(Table, blocked.VfsPath);
        Assert.Throws<InvalidOperationException>(() => ModMerger.Write(plan, Path.Combine(root, "out", "Merged"), "Merged"));
        Assert.Contains(MergeReport.Lines(plan, null, 10), l => l.Contains("Result: cannot merge"));
    }

    [Fact]
    public void Plan_UnknownMod_Throws()
    {
        var (install, _) = NewInstall("1,a\r\n", "1,a\r\n", "1,a\r\n");

        Assert.Throws<DirectoryNotFoundException>(() => new ModMerger(install).Plan(["A", "Missing"]));
    }
}
