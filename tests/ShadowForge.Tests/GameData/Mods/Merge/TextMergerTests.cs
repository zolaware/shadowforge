using System.Text;
using ShadowForge.GameData.Mods.Merge;

namespace ShadowForge.Tests.GameData.Mods.Merge;

public sealed class TextMergerTests
{
    private static readonly FormatMergers Formats = new(GameNames.Empty);

    internal static byte[] UTF16(string text) => [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)];

    private static FileMergeResult Merge(string fileName, byte[] stock, params (string Mod, byte[] Data)[] versions) =>
        Formats.Merge(fileName, stock, versions.Select(v => new ModVersion(v.Mod, v.Data)).ToList());

    [Fact]
    public void Merge_U16RowsChangedByDifferentMods_KeepsBothAndTheByteOrderMark()
    {
        byte[] stock = UTF16("1,a\r\n2,b\r\n3,c\r\n");

        var result = Merge("ItTb.u16", stock, ("A", UTF16("1,A\r\n2,b\r\n3,c\r\n")), ("B", UTF16("1,a\r\n2,b\r\n3,C\r\n")));

        Assert.True(result.IsMerged);
        Assert.Equal(UTF16("1,A\r\n2,b\r\n3,C\r\n"), result.Data);
    }

    [Fact]
    public void Merge_RowInsertedAboveTheFirstRow_KeepsTheByteOrderMarkFirst()
    {
        byte[] stock = UTF16("1,a\r\n2,b\r\n");

        var result = Merge("x.u16", stock, ("A", UTF16("0,new\r\n1,a\r\n2,b\r\n")), ("B", UTF16("1,a\r\n2,B\r\n")));

        Assert.Equal(UTF16("0,new\r\n1,a\r\n2,B\r\n"), result.Data);
    }

    [Fact]
    public void Merge_ShiftJISBytes_AreCarriedThroughUnchanged()
    {
        byte[] Sjis(string ascii) => [0x82, 0xA0, .. Encoding.ASCII.GetBytes(ascii)];
        byte[] stock = Sjis(",x\r\nrow2\r\nrow3\r\n");

        var result = Merge("motscr_em001.csv", stock, ("A", Sjis(",x\r\nROW2\r\nrow3\r\n")), ("B", Sjis(",x\r\nrow2\r\nROW3\r\n")));

        Assert.Equal(Sjis(",x\r\nROW2\r\nROW3\r\n"), result.Data);
    }

    [Fact]
    public void Merge_AppendingAfterAnUnterminatedLastLine_IsAnInsertion()
    {
        byte[] stock = Encoding.ASCII.GetBytes("a\r\nb");

        var result = Merge("x.csv", stock,
            ("A", Encoding.ASCII.GetBytes("A\r\nb")), ("B", Encoding.ASCII.GetBytes("a\r\nb\r\nc")));

        Assert.Equal("A\r\nb\r\nc", Encoding.ASCII.GetString(result.Data!));
    }

    [Fact]
    public void Merge_TwoModsAppendingDifferentRows_Conflict()
    {
        byte[] stock = UTF16("1,a\r\n");

        var result = Merge("x.u16", stock, ("A", UTF16("1,a\r\n2,from A\r\n")), ("B", UTF16("1,a\r\n2,from B\r\n")));

        Assert.False(result.IsMerged);
        Assert.Single(result.Conflicts);
    }

    [Fact]
    public void Merge_KeyedSkillTable_MatchesRowsByIdNotPosition()
    {
        static string Row(string id, string value) => string.Join(",", Enumerable.Repeat(value, 17)) + "," + id;
        byte[] stock = UTF16($"{Row("10", "0")}\r\n{Row("20", "0")}\r\n");

        var result = Merge("SkTb_new.u16", stock,
            ("A", UTF16($"{Row("5", "9")}\r\n{Row("10", "0")}\r\n{Row("20", "0")}\r\n")),
            ("B", UTF16($"{Row("10", "0")}\r\n{Row("20", "7")}\r\n")));

        Assert.True(result.IsMerged);
        Assert.Equal(UTF16($"{Row("10", "0")}\r\n{Row("20", "7")}\r\n{Row("5", "9")}\r\n"), result.Data);
    }

    [Fact]
    public void Merge_InvalidUTF16InAModsTable_CannotBeCompared()
    {
        byte[] stock = UTF16("1,a\r\n");

        var result = Merge("x.u16", stock, ("A", UTF16("1,b\r\n")), ("B", [0xFE, 0xFF, 0xD8, 0x00]));

        Assert.False(result.IsMerged);
        Assert.Contains("B's copy is not valid", result.Problem);
    }

    [Fact]
    public void Merge_EnemyRowChangedByTwoMods_NamesTheEnemyAndTheColumns()
    {
        var names = GameNames.FromTables([], [UTF16("DeleteKey,ID,Name\r\n,em003,Test Enemy\r\n")]);
        const string Header = "No.,category,size,grid,name,race,level,hp\r\n";
        byte[] stock = UTF16(Header + "003,1,S,,test,x,5,2250\r\n");

        var result = new FormatMergers(names).Merge("enemy_zako_new.u16", stock,
        [
            new ModVersion("A", UTF16(Header + "003,1,S,,test,x,5,3000\r\n")),
            new ModVersion("B", UTF16(Header + "003,1,S,,test,x,5,9999\r\n")),
        ]);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("enemy em003 Test Enemy", conflict.Location);
        Assert.Equal(["HP 2250 -> 3000", "HP 2250 -> 9999"], conflict.Sides.Select(s => s.Value));
    }

    [Fact]
    public void Merge_ItemRowChangedByTwoMods_NamesTheItem()
    {
        var names = GameNames.FromTables([UTF16("First Item,x\r\nSecond Item,x\r\n")], []);
        byte[] stock = UTF16("4,2,30\r\n4,2,100\r\n");

        var result = new FormatMergers(names).Merge("ItTb.u16", stock,
        [
            new ModVersion("A", UTF16("4,2,30\r\n4,2,50\r\n")),
            new ModVersion("B", UTF16("4,2,30\r\n4,2,75\r\n")),
        ]);

        Assert.Equal("item 2 Second Item", Assert.Single(result.Conflicts).Location);
    }
}
