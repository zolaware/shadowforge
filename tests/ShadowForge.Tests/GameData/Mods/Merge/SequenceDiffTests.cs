using ShadowForge.GameData.Mods.Merge;

namespace ShadowForge.Tests.GameData.Mods.Merge;

public sealed class SequenceDiffTests
{
    [Fact]
    public void Compute_IdenticalSequences_HasNoHunks()
    {
        Assert.Empty(SequenceDiff.Compute(["a", "b", "c"], ["a", "b", "c"]));
    }

    [Fact]
    public void Compute_Insertion_IsAnEmptyBaseRange()
    {
        var hunk = Assert.Single(SequenceDiff.Compute(["a", "c"], ["a", "b", "c"]));

        Assert.Equal(new DiffHunk(1, 1, 1, 2), hunk);
        Assert.True(hunk.IsInsertion);
    }

    [Fact]
    public void Compute_Deletion_IsAnEmptyOtherRange()
    {
        var hunk = Assert.Single(SequenceDiff.Compute(["a", "b", "c"], ["a", "c"]));

        Assert.Equal(new DiffHunk(1, 2, 1, 1), hunk);
    }

    [Fact]
    public void Compute_SeparateEdits_GiveSeparateHunks()
    {
        var hunks = SequenceDiff.Compute(["a", "b", "c", "d", "e"], ["a", "B", "c", "d", "E", "f"]);

        Assert.Equal([new DiffHunk(1, 2, 1, 2), new DiffHunk(4, 5, 4, 6)], hunks);
    }

    [Fact]
    public void Compute_IsMinimal()
    {
        string[] a = ["a", "b", "c", "a", "b", "b", "a"];
        string[] b = ["c", "b", "a", "b", "a", "c"];

        int edits = SequenceDiff.Compute(a, b).Sum(h => (h.BaseEnd - h.BaseStart) + (h.OtherEnd - h.OtherStart));

        Assert.Equal(5, edits);
    }

    [Fact]
    public void Compute_EmptyBase_IsOneInsertion()
    {
        Assert.Equal([new DiffHunk(0, 0, 0, 2)], SequenceDiff.Compute([], ["a", "b"]));
    }
}
