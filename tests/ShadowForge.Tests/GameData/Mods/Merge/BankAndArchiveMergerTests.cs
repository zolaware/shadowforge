using ShadowForge.Formats.IPK;
using ShadowForge.Formats.XACT;
using ShadowForge.GameData.Mods.Merge;
using ShadowForge.IO;
using ShadowForge.Tests.Formats.XACT;

namespace ShadowForge.Tests.GameData.Mods.Merge;

public sealed class WaveBankMergerTests
{
    private static readonly FormatMergers Formats = new(GameNames.Empty);

    private static byte[] WithWave(byte[] stock, int index, byte fill)
    {
        var bank = WaveBank.Read(stock, "test");
        bank.Entries[index].Data = Enumerable.Repeat(fill, 200).ToArray();
        return bank.Write();
    }

    [Fact]
    public void Merge_DifferentWavesReplaced_KeepsBoth()
    {
        byte[] stock = WaveBankTests.BuildBank();

        var result = Formats.Merge("BG_01.xwb", stock,
            [new ModVersion("A", WithWave(stock, 0, 0xAA)), new ModVersion("B", WithWave(stock, 2, 0xBB))]);

        var merged = WaveBank.Read(result.Data!, "merged");
        Assert.All(merged.Entries[0].Data, b => Assert.Equal(0xAA, b));
        Assert.Equal(WaveBank.Read(stock, "stock").Entries[1].Data, merged.Entries[1].Data);
        Assert.All(merged.Entries[2].Data, b => Assert.Equal(0xBB, b));
    }

    [Fact]
    public void Merge_OneWaveReplacedTwice_Conflict()
    {
        byte[] stock = WaveBankTests.BuildBank();

        var result = Formats.Merge("BG_01.xwb", stock,
            [new ModVersion("A", WithWave(stock, 1, 0xAA)), new ModVersion("B", WithWave(stock, 1, 0xBB))]);

        Assert.Equal("wave 1", Assert.Single(result.Conflicts).Location);
    }
}

public sealed class SoundBankMergerTests
{
    private const string ShippedBank = @"snd_memory\se\fdenm.xsb";

    private static byte[] WithCueWave(byte[] bank, SoundCue cue, ushort wave)
    {
        byte[] copy = bank.ToArray();
        BigEndian.WriteUInt16(copy, cue.WaveReferenceOffset, wave);
        return copy;
    }

    [SkippableFact]
    public void Merge_DifferentCuesRemapped_KeepsBoth()
    {
        byte[] stock = RetailData.Read(ShippedBank);
        var cues = SoundBank.Read(stock, ShippedBank).Cues
            .GroupBy(c => c.WaveReferenceOffset).Select(g => g.First()).ToList();
        Skip.If(cues.Count < 2, "the bank needs two cues with their own sound records");

        var result = new FormatMergers(GameNames.Empty).Merge("se_even596.xsb", stock,
            [new ModVersion("A", WithCueWave(stock, cues[0], 7)), new ModVersion("B", WithCueWave(stock, cues[1], 9))]);

        var merged = SoundBank.Read(result.Data!, "merged");
        Assert.Equal(7, merged.Cues[0].WaveIndex);
        Assert.Equal(9, merged.Cues[1].WaveIndex);
    }

    [SkippableFact]
    public void Merge_ACopyChangingMoreThanWaveReferences_CannotBeCompared()
    {
        byte[] stock = RetailData.Read(ShippedBank);
        var cue = SoundBank.Read(stock, ShippedBank).Cues[0];
        byte[] renamed = stock.ToArray();
        renamed[SoundBank.BankNameOffset] ^= 0x01;

        var result = new FormatMergers(GameNames.Empty).Merge("se_even596.xsb", stock,
            [new ModVersion("A", WithCueWave(stock, cue, 7)), new ModVersion("B", renamed)]);

        Assert.Contains("changes more than which wave", result.Problem);
    }
}

public sealed class ArchiveMergerTests
{
    private static byte[] Archive(params (string Name, string Text)[] entries) =>
        ArchiveWriter.Build(entries.Select(e => new ArchiveWriter.InputEntry(e.Name, TextMergerTests.UTF16(e.Text))).ToList());

    private static Dictionary<string, byte[]> Entries(byte[] archive) =>
        ArchiveReader.ReadAllEntries(archive).Entries.ToDictionary(e => e.Name, e => e.Data);

    [Fact]
    public void Merge_TableInsideTheArchive_MergesRowByRow()
    {
        byte[] stock = Archive(("t.u16", "1,a\r\n2,b\r\n"), ("other.u16", "x\r\n"));

        var result = new FormatMergers(GameNames.Empty).Merge("DL_Param.ipk", stock,
        [
            new ModVersion("A", Archive(("t.u16", "1,A\r\n2,b\r\n"), ("other.u16", "x\r\n"))),
            new ModVersion("B", Archive(("t.u16", "1,a\r\n2,B\r\n"), ("other.u16", "x\r\n"), ("new.u16", "n\r\n"))),
        ]);

        var entries = Entries(result.Data!);
        Assert.Equal(["t.u16", "other.u16", "new.u16"], entries.Keys);
        Assert.Equal(TextMergerTests.UTF16("1,A\r\n2,B\r\n"), entries["t.u16"]);
    }

    [Fact]
    public void Merge_ConflictInsideAnEntry_IsReportedUnderTheEntryName()
    {
        byte[] stock = Archive(("t.u16", "1,a\r\n"));

        var result = new FormatMergers(GameNames.Empty).Merge("x.ipk", stock,
            [new ModVersion("A", Archive(("t.u16", "1,x\r\n"))), new ModVersion("B", Archive(("t.u16", "1,y\r\n")))]);

        Assert.StartsWith("t.u16: ", Assert.Single(result.Conflicts).Location);
    }
}
