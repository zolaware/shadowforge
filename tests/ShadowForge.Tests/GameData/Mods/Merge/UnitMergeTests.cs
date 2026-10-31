using ShadowForge.GameData.Mods.Merge;

namespace ShadowForge.Tests.GameData.Mods.Merge;

public sealed class UnitMergeTests
{
    private static readonly string[] Stock = ["a", "b", "c", "d", "e"];

    private static SequenceMergeResult Merge(params (string Mod, string[] Units)[] versions) =>
        UnitMerge.MergeSequences(Stock, versions.Select(v => new ModUnits(v.Mod, v.Units)).ToList());

    [Fact]
    public void MergeSequences_EditsToDifferentUnits_BothApply()
    {
        var result = Merge(("A", ["a", "B", "c", "d", "e"]), ("B", ["a", "b", "c", "D", "e"]));

        Assert.Empty(result.Conflicts);
        Assert.Equal(["a", "B", "c", "D", "e"], result.Units);
    }

    [Fact]
    public void MergeSequences_TheSameEditTwice_AppliesOnce()
    {
        var result = Merge(("A", ["a", "B", "c", "d", "e"]), ("B", ["a", "B", "c", "d", "e"]));

        Assert.Empty(result.Conflicts);
        Assert.Equal(["a", "B", "c", "d", "e"], result.Units);
    }

    [Fact]
    public void MergeSequences_DifferentEditsToOneUnit_Conflict()
    {
        var result = Merge(("A", ["a", "X", "c", "d", "e"]), ("B", ["a", "Y", "c", "d", "e"]));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal((1, 2), (conflict.StockStart, conflict.StockEnd));
        Assert.Equal(["A", "B"], conflict.Sides.Select(s => s.Mod));
        Assert.Equal(Stock, result.Units);
    }

    [Fact]
    public void MergeSequences_OverlappingRewrites_ConflictOnlyWhereTheyDiffer()
    {
        var result = Merge(("A", ["a", "B", "C", "d", "e"]), ("B", ["a", "b", "C", "D", "e"]));

        Assert.Empty(result.Conflicts);
        Assert.Equal(["a", "B", "C", "D", "e"], result.Units);
    }

    [Fact]
    public void MergeSequences_InsertionsAtDifferentPlaces_BothApply()
    {
        var result = Merge(("A", ["a", "x", "b", "c", "d", "e"]), ("B", ["a", "b", "c", "d", "y", "e"]));

        Assert.Empty(result.Conflicts);
        Assert.Equal(["a", "x", "b", "c", "d", "y", "e"], result.Units);
    }

    [Fact]
    public void MergeSequences_DifferentInsertionsAtOnePlace_Conflict()
    {
        var result = Merge(("A", ["a", "b", "c", "d", "e", "x"]), ("B", ["a", "b", "c", "d", "e", "y"]));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal((5, 5), (conflict.StockStart, conflict.StockEnd));
    }

    [Fact]
    public void MergeSequences_InsertionNextToAnotherModsEdit_BothApply()
    {
        var result = Merge(("A", ["a", "B", "c", "d", "e"]), ("B", ["a", "b", "x", "c", "d", "e"]));

        Assert.Empty(result.Conflicts);
        Assert.Equal(["a", "B", "x", "c", "d", "e"], result.Units);
    }

    [Fact]
    public void MergeKeyed_DifferentKeys_BothApplyAndNewKeysAppendInModOrder()
    {
        var stock = Units(("1", "a"), ("2", "b"));
        var result = UnitMerge.MergeKeyed(stock,
        [
            new ModKeyedUnits("A", Units(("1", "A"), ("2", "b"), ("3", "new from A"))),
            new ModKeyedUnits("B", Units(("1", "a"), ("2", "B"), ("4", "new from B"))),
        ]);

        Assert.Empty(result.Conflicts);
        Assert.Equal(["1=A", "2=B", "3=new from A", "4=new from B"], result.Units.Select(u => $"{u.Key}={u.Value}"));
    }

    [Fact]
    public void MergeKeyed_OneKeySetTwoWays_Conflict()
    {
        var result = UnitMerge.MergeKeyed(Units(("1", "a")),
        [
            new ModKeyedUnits("A", Units(("1", "x"))),
            new ModKeyedUnits("B", Units(("1", "y"))),
        ]);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("1", conflict.Key);
        Assert.Equal("a", conflict.Stock);
        Assert.Equal(["x", "y"], conflict.Sides.Select(s => s.Value));
    }

    [Fact]
    public void MergeKeyed_RemovedKey_IsDropped()
    {
        var result = UnitMerge.MergeKeyed(Units(("1", "a"), ("2", "b")),
        [
            new ModKeyedUnits("A", Units(("1", "a"))),
            new ModKeyedUnits("B", Units(("1", "a"), ("2", "b"))),
        ]);

        Assert.Equal(["1"], result.Units.Select(u => u.Key));
    }

    private static List<KeyValuePair<string, string>> Units(params (string Key, string Value)[] units) =>
        units.Select(u => new KeyValuePair<string, string>(u.Key, u.Value)).ToList();
}
