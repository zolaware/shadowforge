using System.Text.RegularExpressions;
using ShadowForge.Formats.BDSL;
using ShadowForge.Formats.RPJ;
using ShadowForge.GameData.Mods.Merge;

namespace ShadowForge.Tests.GameData.Mods.Merge;

public sealed partial class SceneScriptMergerTests
{
    private const string ShippedScript = @"script\town\sc01_0011_02.rpj";

    [GeneratedRegex(@"^(\s+var\[\d+\] = )\d+\s*$")]
    private static partial Regex Assignment();

    private static List<string> Decompile(byte[] rpj) => TextScene.Write(BinaryScene.Read(rpj)).Split('\n').ToList();

    private static byte[] WithAssignment(List<string> lines, int index, int value)
    {
        var copy = lines.ToList();
        copy[index] = Assignment().Replace(copy[index].TrimEnd('\r'), $"${{1}}{value}");
        return BinaryScene.Write(TextScene.Read(string.Join('\n', copy)));
    }

    private static (byte[] Stock, List<string> Lines, List<int> Assignments) ShippedAssignments()
    {
        byte[] stock = RetailData.Read(ShippedScript);
        var lines = Decompile(stock);
        var assignments = Enumerable.Range(0, lines.Count).Where(i => Assignment().IsMatch(lines[i])).ToList();
        Skip.If(assignments.Count < 2, "the script needs two variable assignments");
        return (stock, lines, assignments);
    }

    [SkippableFact]
    public void Merge_DifferentStatementsChanged_KeepsBothAndCompiles()
    {
        var (stock, lines, assignments) = ShippedAssignments();
        int first = assignments[0], last = assignments[^1];

        var result = new FormatMergers(GameNames.Empty).Merge("sc01_0011_02.rpj", stock,
        [
            new ModVersion("A", WithAssignment(lines, first, 7001)),
            new ModVersion("B", WithAssignment(lines, last, 7002)),
        ]);

        var merged = Decompile(result.Data!);
        Assert.EndsWith("= 7001", merged[first].TrimEnd('\r'));
        Assert.EndsWith("= 7002", merged[last].TrimEnd('\r'));
    }

    [SkippableFact]
    public void Merge_OneStatementChangedTwoWays_ConflictNamesTheLine()
    {
        var (stock, lines, assignments) = ShippedAssignments();
        int line = assignments[0];

        var result = new FormatMergers(GameNames.Empty).Merge("sc01_0011_02.rpj", stock,
        [
            new ModVersion("A", WithAssignment(lines, line, 7001)),
            new ModVersion("B", WithAssignment(lines, line, 7002)),
        ]);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal($"script line {line + 1}", conflict.Location);
        Assert.EndsWith("= 7001", conflict.Sides[0].Value);
    }
}
