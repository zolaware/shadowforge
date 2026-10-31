using ShadowForge.Formats.XACT;
using ShadowForge.IO;

namespace ShadowForge.Tests.Formats.XACT;

public sealed class WaveFormatTests
{
    [Fact]
    public void Unpack_RetailPcmWord()
    {
        var format = new WaveFormat(0x81177004);
        Assert.Equal(WaveFormatTag.PCM, format.Tag);
        Assert.Equal(1, format.Channels);
        Assert.Equal(48000, format.SamplesPerSecond);
        Assert.Equal(2, format.BlockAlign);
        Assert.Equal(16, format.BitsPerSample);
    }

    [Fact]
    public void Unpack_RetailXmaWord()
    {
        var format = new WaveFormat(0x81177005);
        Assert.Equal(WaveFormatTag.XMA, format.Tag);
        Assert.Equal(1, format.Channels);
        Assert.Equal(48000, format.SamplesPerSecond);
    }

    [Fact]
    public void Pack_Pcm16_MatchesRetailWord()
    {
        Assert.Equal(0x81177004u, WaveFormat.Pcm16(1, 48000).Packed);
        Assert.Equal(0x82177008u, WaveFormat.Pcm16(2, 48000).Packed);
    }

    [Fact]
    public void Pack_RejectsUnrepresentableChannelCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WaveFormat.Pcm16(9, 48000));
    }
}

public sealed class WaveBankTests
{
    private const string ShippedPcmBank = @"snd_memory\se\se_even596.xwb";
    private const string ShippedXmaBank = @"snd_memory\se\fdenm.xwb";

    private const uint Alignment = 2048;
    private const uint WaveDataOffset = 2048;

    [Fact]
    public void Read_Synthetic_DecodesEveryEntry()
    {
        var bank = WaveBank.Read(BuildBank(), "synthetic");

        Assert.Equal(40u, bank.Version);
        Assert.Equal("synth", bank.Name);
        Assert.Equal(Alignment, bank.Alignment);
        Assert.Equal(3, bank.Entries.Count);

        Assert.Equal(WaveFormatTag.PCM, bank.Entries[0].Format.Tag);
        Assert.Equal(50u, bank.Entries[0].DurationSamples);
        Assert.Equal(100, bank.Entries[0].Data.Length);

        Assert.Equal(WaveFormatTag.XMA, bank.Entries[1].Format.Tag);
        Assert.Equal(3000, bank.Entries[1].Data.Length);

        Assert.Equal(500, bank.Entries[2].Data.Length);
    }

    /// <summary>
    /// The duration lives in the upper 28 bits, so a 178562
    /// byte mono 16-bit entry stores 0x0015CC10 for its 89281 samples.
    /// </summary>
    [Fact]
    public void FlagsAndDuration_SplitsOnTheLowFourBits()
    {
        Assert.Equal(89281u, 0x0015CC10u >> 4);

        var entry = new WaveBankEntry { DurationSamples = 89281, Flags = 0 };
        Assert.Equal(0x0015CC10u, entry.FlagsAndDuration);
    }

    [Fact]
    public void Write_RoundTripsUntouchedEntries()
    {
        var original = WaveBank.Read(BuildBank(), "synthetic");
        var rebuilt = WaveBank.Read(original.Write(), "rebuilt");

        Assert.Equal(original.Entries.Count, rebuilt.Entries.Count);
        for (int i = 0; i < original.Entries.Count; i++)
        {
            Assert.Equal(original.Entries[i].Data, rebuilt.Entries[i].Data);
            Assert.Equal(original.Entries[i].Format.Packed, rebuilt.Entries[i].Format.Packed);
            Assert.Equal(original.Entries[i].DurationSamples, rebuilt.Entries[i].DurationSamples);
        }
        Assert.Equal(original.BankData, rebuilt.BankData);
        Assert.Equal(original.SeekTables, rebuilt.SeekTables);
        Assert.Equal(original.EntryNames, rebuilt.EntryNames);
    }

    [Fact]
    public void ReplaceEntry_LeavesTheOtherEntriesAndSegmentsAlone()
    {
        var bank = WaveBank.Read(BuildBank(), "synthetic");
        byte[] entry0 = bank.Entries[0].Data;
        byte[] entry2 = bank.Entries[2].Data;
        byte[] seekTables = bank.SeekTables;
        byte[] bankData = bank.BankData;

        var wav = new WavFile { Channels = 1, SampleRate = 44100, Samples = Pattern(9000) };
        bank.ReplaceEntry(1, wav);
        var rebuilt = WaveBank.Read(bank.Write(), "rebuilt");

        Assert.Equal(entry0, rebuilt.Entries[0].Data);
        Assert.Equal(entry2, rebuilt.Entries[2].Data);
        Assert.Equal(seekTables, rebuilt.SeekTables);
        Assert.Equal(bankData, rebuilt.BankData);

        var replaced = rebuilt.Entries[1];
        Assert.Equal(WaveFormatTag.PCM, replaced.Format.Tag);
        Assert.Equal(1, replaced.Format.Channels);
        Assert.Equal(44100, replaced.Format.SamplesPerSecond);
        Assert.Equal(2, replaced.Format.BlockAlign);
        Assert.Equal(16, replaced.Format.BitsPerSample);
        Assert.Equal(4500u, replaced.DurationSamples);
        Assert.Equal(0u, replaced.Flags);
        Assert.Equal(0u, replaced.LoopRegionStartSample);
        Assert.Equal(0u, replaced.LoopRegionTotalSamples);
    }

    [Fact]
    public void ReplaceEntry_StoresPcmBigEndian()
    {
        var bank = WaveBank.Read(BuildBank(), "synthetic");
        var wav = new WavFile { Channels = 1, SampleRate = 48000, Samples = [0x01, 0x02, 0x03, 0x04] };
        bank.ReplaceEntry(0, wav);

        Assert.Equal(new byte[] { 0x02, 0x01, 0x04, 0x03 }, bank.Entries[0].Data);

        var exported = WavFile.FromBigEndianPcm(bank.Entries[0].Data, 1, 48000);
        Assert.Equal(wav.Samples, exported.Samples);
    }

    [Fact]
    public void Write_KeepsEveryPlayRegionAligned()
    {
        var bank = WaveBank.Read(BuildBank(), "synthetic");
        bank.ReplaceEntry(0, new WavFile { Channels = 2, SampleRate = 48000, Samples = Pattern(1234) });
        byte[] written = bank.Write();

        var rebuilt = WaveBank.Read(written, "rebuilt");
        uint metaOffset = rebuilt.SegmentOffsets[WaveBank.SegmentEntryMetaData];
        long total = 0;
        for (int i = 0; i < rebuilt.Entries.Count; i++)
        {
            int record = (int)(metaOffset + i * WaveBankEntry.RecordSize);
            uint offset = BigEndian.ReadUInt32(written, record + WaveBankEntry.PlayRegionOffsetOffset);
            Assert.Equal(0u, offset % Alignment);
            Assert.Equal(total, (long)offset);
            total += (rebuilt.Entries[i].Data.Length + Alignment - 1) / Alignment * Alignment;
        }
        Assert.Equal(WaveDataOffset + total, (long)written.Length);
    }

    [Fact]
    public void Read_WrongSignature_Throws()
    {
        var data = BuildBank();
        data[0] = (byte)'W';
        Assert.Throws<InvalidDataException>(() => WaveBank.Read(data, "corrupt"));
    }

    [SkippableFact]
    public void Read_ShippedPcmBank_MatchesTheDurationRule()
    {
        var bank = WaveBank.Read(RetailData.Read(ShippedPcmBank), "se_even596.xwb");

        var entry = bank.Entries[0];
        Assert.Equal(WaveFormatTag.PCM, entry.Format.Tag);
        Assert.Equal(1, entry.Format.Channels);
        Assert.Equal(48000, entry.Format.SamplesPerSecond);
        Assert.Equal(178562, entry.Data.Length);
        Assert.Equal(89281u, entry.DurationSamples);
        Assert.Equal((uint)(entry.Data.Length / (2 * entry.Format.Channels)), entry.DurationSamples);
    }

    /// <summary>
    /// A rebuild with nothing replaced reproduces the shipped bank byte for
    /// byte, pinning the segment and padding arithmetic.
    /// </summary>
    [SkippableFact]
    public void Write_ShippedBank_IsByteIdentical()
    {
        byte[] original = RetailData.Read(ShippedXmaBank);
        var bank = WaveBank.Read(original, "fdenm.xwb");
        Assert.Equal(86, bank.Entries.Count);
        Assert.Equal(original, bank.Write());
    }

    /// <summary>
    /// A 360 bank stores PCM big-endian. Decoded that way a shipped wave is a
    /// smooth signal; decoded little-endian it is noise.
    /// </summary>
    [SkippableFact]
    public void ShippedPcm_IsBigEndian()
    {
        byte[] data = WaveBank.Read(RetailData.Read(ShippedPcmBank), "se_even596.xwb").Entries[0].Data;

        double bigEndian = Autocorrelation(data, swap: true);
        double littleEndian = Autocorrelation(data, swap: false);
        Assert.True(bigEndian > 0.99, $"big-endian autocorrelation was {bigEndian}");
        Assert.True(littleEndian < 0.5, $"little-endian autocorrelation was {littleEndian}");
    }

    private static double Autocorrelation(byte[] data, bool swap)
    {
        byte[] pcm = swap ? WavFile.SwapSamples(data) : data;
        int count = Math.Min(pcm.Length / 2, 40000);
        var samples = new double[count];
        for (int i = 0; i < count; i++)
            samples[i] = BitConverter.ToInt16(pcm, i * 2);

        double mean = samples.Average();
        double numerator = 0, denominator = 0;
        for (int i = 0; i < count; i++)
        {
            double centered = samples[i] - mean;
            denominator += centered * centered;
            if (i + 1 < count) numerator += centered * (samples[i + 1] - mean);
        }
        return denominator == 0 ? 0 : numerator / denominator;
    }

    /// <summary>
    /// A three-entry bank laid out the way the retail banks are: a 0x60 bank
    /// data segment, 24-byte entry records, a seek table to preserve and no
    /// entry names.
    /// </summary>
    internal static byte[] BuildBank()
    {
        const int bankDataOffset = WaveBank.HeaderSize;
        const int metaOffset = bankDataOffset + WaveBank.BankDataSize;
        const int entryCount = 3;
        const int seekOffset = metaOffset + entryCount * WaveBankEntry.RecordSize;
        const int seekLength = 16;

        var entries = new (uint Format, uint Duration, byte[] Data)[]
        {
            (WaveFormat.Pcm16(1, 48000).Packed, 50, Pattern(100)),
            (WaveFormat.Pack(WaveFormatTag.XMA, 1, 48000, 2, 16).Packed, 12000, Pattern(3000)),
            (WaveFormat.Pcm16(2, 48000).Packed, 125, Pattern(500)),
        };

        long waveTotal = 0;
        var playOffsets = new uint[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            playOffsets[i] = (uint)waveTotal;
            waveTotal += (entries[i].Data.Length + Alignment - 1) / Alignment * Alignment;
        }

        var data = new byte[WaveDataOffset + waveTotal];
        data[0] = 0x44; data[1] = 0x4E; data[2] = 0x42; data[3] = 0x57;
        BigEndian.WriteUInt32(data, WaveBank.VersionOffset, 40);

        var segments = new (uint Offset, uint Length)[]
        {
            (bankDataOffset, WaveBank.BankDataSize),
            (metaOffset, entryCount * WaveBankEntry.RecordSize),
            (seekOffset, seekLength),
            (0, 0),
            (WaveDataOffset, (uint)waveTotal),
        };
        for (int i = 0; i < segments.Length; i++)
        {
            BigEndian.WriteUInt32(data, WaveBank.SegmentTableOffset + i * 8, segments[i].Offset);
            BigEndian.WriteUInt32(data, WaveBank.SegmentTableOffset + i * 8 + 4, segments[i].Length);
        }

        BigEndian.WriteUInt32(data, bankDataOffset + WaveBank.BankFlagsOffset, 0x00080000);
        BigEndian.WriteUInt32(data, bankDataOffset + WaveBank.BankEntryCountOffset, entryCount);
        "synth"u8.CopyTo(data.AsSpan(bankDataOffset + WaveBank.BankNameOffset));
        BigEndian.WriteUInt32(data, bankDataOffset + WaveBank.BankEntryMetaDataElementSizeOffset,
                              WaveBankEntry.RecordSize);
        BigEndian.WriteUInt32(data, bankDataOffset + WaveBank.BankEntryNameElementSizeOffset, 64);
        BigEndian.WriteUInt32(data, bankDataOffset + WaveBank.BankAlignmentOffset, Alignment);

        for (int i = 0; i < seekLength; i++) data[seekOffset + i] = (byte)(0xA0 + i);

        for (int i = 0; i < entryCount; i++)
        {
            int record = metaOffset + i * WaveBankEntry.RecordSize;
            BigEndian.WriteUInt32(data, record + WaveBankEntry.FlagsAndDurationOffset, entries[i].Duration << 4);
            BigEndian.WriteUInt32(data, record + WaveBankEntry.FormatOffset, entries[i].Format);
            BigEndian.WriteUInt32(data, record + WaveBankEntry.PlayRegionOffsetOffset, playOffsets[i]);
            BigEndian.WriteUInt32(data, record + WaveBankEntry.PlayRegionLengthOffset, (uint)entries[i].Data.Length);
            Array.Copy(entries[i].Data, 0, data, WaveDataOffset + playOffsets[i], entries[i].Data.Length);
        }

        return data;
    }

    private static byte[] Pattern(int length)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++) data[i] = (byte)(i * 7 + 3);
        return data;
    }
}
