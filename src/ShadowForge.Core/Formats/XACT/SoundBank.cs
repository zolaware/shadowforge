using ShadowForge.IO;

namespace ShadowForge.Formats.XACT;

/// <summary>
/// An Xbox 360 XACT sound bank (.xsb). The file is big-endian, its signature reads "KBDS"
/// ("SDBK" byte-reversed) and header fields sit at unaligned offsets. Only the mapping from
/// cue name to wave index is decoded, the part a wave replacement needs.
/// </summary>
public sealed class SoundBank
{
    public static ReadOnlySpan<byte> Signature => [0x4B, 0x42, 0x44, 0x53];

    public const int ToolVersionOffset = 0x04;
    public const int SimpleCueCountOffset = 0x09;
    public const int ComplexCueCountOffset = 0x0B;
    public const int SimpleCuesOffset = 0x1A;
    public const int ComplexCuesOffset = 0x1E;
    public const int CueNamesOffset = 0x22;
    public const int WaveBankNameTableOffset = 0x32;
    public const int CueNameHashTableOffset = 0x36;
    public const int CueNameHashValuesOffset = 0x3A;
    public const int SoundsOffset = 0x3E;
    public const int BankNameOffset = 0x4A;
    public const int BankNameFieldWidth = 64;

    /// <summary>The complex cue table offset when the bank has no complex cues.</summary>
    public const uint NoComplexCues = 0xFFFFFFFF;

    public const int SimpleCueRecordSize = 5;
    public const int SimpleCueSoundOffsetOffset = 0x01;

    /// <summary>
    /// Stride of the cue name value table: a u32 name offset plus a u16 link.
    /// </summary>
    public const int CueNameValueRecordSize = 6;

    public const int SoundFlagsOffset = 0x00;
    public const int SoundCategoryOffset = 0x01;
    public const int SoundVolumeOffset = 0x03;
    public const int SoundPitchOffset = 0x04;
    public const int SoundEntryLengthOffset = 0x08;

    /// <summary>Bit 0 of the sound record's flags byte selects a complex sound.</summary>
    public const byte SoundFlagComplex = 0x01;

    public const int SimpleSoundWaveIndexOffset = 0x09;
    public const int SimpleSoundWaveBankIndexOffset = 0x0B;

    public const int ComplexSoundClipCountOffset = 0x09;

    /// <summary>
    /// Start of a complex sound's clip records. Each is a filler byte plus a u32 clip offset.
    /// </summary>
    public const int ComplexSoundClipTableOffset = 0x0A;

    /// <summary>
    /// The first clip's u32 offset. Clip i's sits <see cref="ComplexSoundClipRecordSize"/> bytes further on.
    /// </summary>
    public const int ComplexSoundClipOffsetOffset = 0x0B;

    public const int ComplexSoundClipRecordSize = 5;

    /// <summary>
    /// The play wave event inside a clip, relative to the clip's own offset. For the
    /// single-clip sounds in these banks the clip is at soundOffset + 0x0F, which puts the
    /// wave index at soundOffset + 0x18.
    /// </summary>
    public const int ClipWaveIndexOffset = 0x09;
    public const int ClipWaveBankIndexOffset = 0x0B;

    /// <summary>A u16 wave index followed by a u8 wave bank index, in sounds and clips alike.</summary>
    public const int WaveReferenceSize = 3;

    public string Name { get; set; } = "";

    /// <summary>Authoring tool version, 40 in retail Blue Dragon banks.</summary>
    public int ToolVersion { get; set; }

    /// <summary>
    /// No retail Blue Dragon bank has complex cues. The count is reported so a bank that
    /// does is not read as if it had none.
    /// </summary>
    public int ComplexCueCount { get; set; }

    public List<string> WaveBankNames { get; } = [];

    public List<SoundCue> Cues { get; } = [];

    public static SoundBank Read(byte[] data, string name)
    {
        if (data.Length < BankNameOffset + BankNameFieldWidth ||
            !data.AsSpan(0, 4).SequenceEqual(Signature))
            throw new InvalidDataException($"{name}: not an Xbox 360 XACT sound bank (SDBK)");

        var bank = new SoundBank
        {
            ToolVersion = BigEndian.ReadUInt16(data, ToolVersionOffset),
            ComplexCueCount = BigEndian.ReadUInt16(data, ComplexCueCountOffset),
            Name = data.DecodeASCII(BankNameOffset, BankNameFieldWidth),
        };

        int simpleCueCount = BigEndian.ReadUInt16(data, SimpleCueCountOffset);
        uint simpleCues = BigEndian.ReadUInt32(data, SimpleCuesOffset);
        uint waveBankNames = BigEndian.ReadUInt32(data, WaveBankNameTableOffset);
        uint cueNameValues = BigEndian.ReadUInt32(data, CueNameHashValuesOffset);

        int waveBankNameCount = (int)((WaveBankNameTableEnd(data, waveBankNames) - waveBankNames) / BankNameFieldWidth);
        for (int i = 0; i < waveBankNameCount; i++)
        {
            long record = waveBankNames + (long)i * BankNameFieldWidth;
            if (record + BankNameFieldWidth > data.Length) break;
            bank.WaveBankNames.Add(data.DecodeASCII((int)record, BankNameFieldWidth));
        }

        for (int i = 0; i < simpleCueCount; i++)
        {
            long cueRecord = simpleCues + (long)i * SimpleCueRecordSize;
            if (cueRecord + SimpleCueRecordSize > data.Length)
                throw new InvalidDataException($"{name}: simple cue {i} runs past the end of the file");
            uint soundOffset = BigEndian.ReadUInt32(data, (int)(cueRecord + SimpleCueSoundOffsetOffset));

            long nameRecord = cueNameValues + (long)i * CueNameValueRecordSize;
            if (nameRecord + CueNameValueRecordSize > data.Length)
                throw new InvalidDataException($"{name}: cue {i} name record runs past the end of the file");
            uint nameOffset = BigEndian.ReadUInt32(data, (int)nameRecord);
            if (nameOffset >= data.Length)
                throw new InvalidDataException($"{name}: cue {i} name offset {nameOffset} is past the end of the file");

            var cue = new SoundCue
            {
                Index = i,
                Name = data.DecodeASCII((int)nameOffset, data.Length - (int)nameOffset),
            };
            ReadSound(data, soundOffset, cue, name);
            bank.Cues.Add(cue);
        }

        return bank;
    }

    public static SoundBank ReadFile(string path)
        => Read(File.ReadAllBytes(path), Path.GetFileName(path));

    /// <summary>
    /// The first cue with this name, or null. Several cues can share one wave.
    /// </summary>
    public SoundCue? FindCue(string cueName)
        => Cues.FirstOrDefault(c => string.Equals(c.Name, cueName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The wave bank name table has no count, so it runs up to the nearest table that starts
    /// after it. In every retail bank that is the sounds table, giving one name per bank and
    /// two for the few that reference a second.
    /// </summary>
    private static long WaveBankNameTableEnd(byte[] data, uint waveBankNames)
    {
        long end = data.Length;
        int[] tableFields =
        [
            SimpleCuesOffset, ComplexCuesOffset, CueNamesOffset,
            CueNameHashTableOffset, CueNameHashValuesOffset, SoundsOffset,
        ];
        foreach (int field in tableFields)
        {
            uint offset = BigEndian.ReadUInt32(data, field);
            if (offset == NoComplexCues) continue;
            if (offset > waveBankNames && offset < end) end = offset;
        }
        return end;
    }

    private static void ReadSound(byte[] data, uint soundOffset, SoundCue cue, string name)
    {
        if (soundOffset + (long)SoundEntryLengthOffset >= data.Length)
            throw new InvalidDataException($"{name}: cue {cue.Index} sound record is past the end of the file");

        cue.IsComplex = (data[soundOffset] & SoundFlagComplex) != 0;

        if (!cue.IsComplex)
        {
            cue.WaveReferenceOffset = (int)(soundOffset + SimpleSoundWaveIndexOffset);
            cue.WaveIndex = BigEndian.ReadUInt16(data, (int)(soundOffset + SimpleSoundWaveIndexOffset));
            cue.WaveBankIndex = data[soundOffset + SimpleSoundWaveBankIndexOffset];
            return;
        }

        cue.ClipCount = data[soundOffset + ComplexSoundClipCountOffset];
        if (cue.ClipCount < 1)
            throw new InvalidDataException($"{name}: cue {cue.Index} is a complex sound with no clips");

        uint clipOffset = BigEndian.ReadUInt32(data, (int)(soundOffset + ComplexSoundClipOffsetOffset));
        if (clipOffset + (long)ClipWaveBankIndexOffset >= data.Length)
            throw new InvalidDataException($"{name}: cue {cue.Index} clip data is past the end of the file");

        cue.WaveReferenceOffset = (int)(clipOffset + ClipWaveIndexOffset);
        cue.WaveIndex = BigEndian.ReadUInt16(data, (int)(clipOffset + ClipWaveIndexOffset));
        cue.WaveBankIndex = data[clipOffset + ClipWaveBankIndexOffset];
    }
}
