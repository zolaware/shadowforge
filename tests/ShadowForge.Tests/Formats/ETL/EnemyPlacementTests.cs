using ShadowForge.Formats.ETL;
using ShadowForge.Formats.IPK;
using ShadowForge.GameData.Mods.Merge;
using ShadowForge.IO;

namespace ShadowForge.Tests.Formats.ETL;

public sealed class EnemyPlacementTests
{
    internal static readonly Formation Pair = new(5, [new Spawn(0x0000_0001, 1, 1), new Spawn(0x0000_0001, 1, 0)]);
    internal static readonly Formation Boss = new(10, [new Spawn(0x0001_0011, 1, 0)]);

    [Fact]
    public void WriteThenRead_KeepsEveryFormation()
    {
        var read = EnemyPlacement.Read(EnemyPlacement.Write([Pair, Boss]));

        Assert.Equal(2, read.Count);
        Assert.Equal(Pair.Weight, read[0].Weight);
        Assert.Equal(Pair.Spawns, read[0].Spawns);
        Assert.Equal(Boss.Spawns, read[1].Spawns);
    }

    [Fact]
    public void Write_NextSpawnOffsetsCountFromTheFirstSpawnRecord()
    {
        byte[] data = EnemyPlacement.Write([Pair, Boss]);
        int firstSpawn = EnemyPlacement.HeaderSize + 2 * EnemyPlacement.FormationRecordSize;

        Assert.Equal((uint)EnemyPlacement.SpawnRecordSize, BigEndian.ReadUInt32(data, firstSpawn + 0x10));
        Assert.Equal(0u, BigEndian.ReadUInt32(data, firstSpawn + EnemyPlacement.SpawnRecordSize + 0x10));
    }

    [Fact]
    public void Write_NoFormations_IsSixteenZeroBytes()
    {
        Assert.Equal(new byte[16], EnemyPlacement.Write([]));
    }

    [Fact]
    public void Spawn_NamesItsTableAndRow()
    {
        Assert.Equal("zako 1", Pair.Spawns[0].ToString());
        Assert.Equal("boss 17", Boss.Spawns[0].ToString());
    }

    [SkippableFact]
    public void ReadThenWrite_EveryShippedTable_RoundTripsByteForByte()
    {
        string scriptPack = Path.Combine(RetailData.Install.GameDataRoot, "pack", "script.ipk");
        Skip.IfNot(File.Exists(scriptPack), "script.ipk is not in the located game data");

        var tables = ArchiveReader.ReadAllEntries(File.ReadAllBytes(scriptPack)).Entries
            .Where(e => e.Name.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(tables);
        Assert.All(tables, t => Assert.Equal(t.Data, EnemyPlacement.Write(EnemyPlacement.Read(t.Data))));
    }
}

public sealed class EnemyPlacementMergerTests
{
    private static readonly Formation SingleZako1 = new(5, [new Spawn(0x0000_0001, 1, 0)]);
    private static readonly Formation SingleZako59 = new(5, [new Spawn(0x0000_003B, 1, 0)]);

    private static FileMergeResult Merge(Formation[] stock, params (string Mod, Formation[] Table)[] versions) =>
        new FormatMergers(GameNames.Empty).Merge("sc03_0001_01.etl", EnemyPlacement.Write(stock),
            versions.Select(v => new ModVersion(v.Mod, EnemyPlacement.Write(v.Table))).ToList());

    [Fact]
    public void Merge_DifferentFormationsChanged_KeepsBoth()
    {
        var result = Merge([EnemyPlacementTests.Pair, EnemyPlacementTests.Boss],
            ("A", [SingleZako1, EnemyPlacementTests.Boss]), ("B", [EnemyPlacementTests.Pair, SingleZako59]));

        Assert.True(result.IsMerged);
        Assert.Equal(EnemyPlacement.Write([SingleZako1, SingleZako59]), result.Data);
    }

    [Fact]
    public void Merge_OneFormationChangedTwoWays_ConflictNamesTheEnemies()
    {
        var result = Merge([EnemyPlacementTests.Pair], ("A", [SingleZako1]), ("B", [SingleZako59]));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("formation 1", conflict.Location);
        Assert.Equal("zako 1 em001 x2", conflict.Stock);
        Assert.Equal(["zako 1 em001", "zako 59 em059"], conflict.Sides.Select(s => s.Value));
    }
}
