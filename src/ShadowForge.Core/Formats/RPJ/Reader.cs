// P:\shadowforge\src\ShadowForge.Core\Formats\RPJ\Reader.cs
using ShadowForge.IO;
using ShadowForge.Text;

namespace ShadowForge.Formats.RPJ;

public static class Reader
{
    public const int HeaderSize = 0x270;
    public const int SectionTableSize = 0x48;
    public const int DataBaseOffset = 0x2B8;
    public const int EntrySize = 0x70;

    public static SceneFile Read(string path)
    {
        EncodingSetup.EnsureRegistered();
        var data = File.ReadAllBytes(path);
        return Read(data);
    }

    public static SceneFile Read(byte[] data)
    {
        EncodingSetup.EnsureRegistered();
        var rpj = new SceneFile { OriginalFileSize = data.Length };

        rpj.Header.RawData = data[..HeaderSize];

        using var ms = new MemoryStream(data);
        using var reader = new BigEndianReader(ms);
        reader.Seek(HeaderSize);
        rpj.Sections = ReadSectionTable(reader);

        ParseEntryPool(rpj, data);
        ParseScriptBlocks(rpj, data);
        ParseWaypoints(rpj, data);
        CaptureInterstitialGaps(rpj, data);

        return rpj;
    }

    private static SectionTable ReadSectionTable(BigEndianReader reader)
    {
        return new SectionTable
        {
            Reserved0 = reader.ReadUInt32(),
            AreaType = (AreaType)reader.ReadUInt32(),
            TotalScriptCmds = reader.ReadUInt32(),
            Reserved1 = [reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()],
            EntryCount = reader.ReadUInt32(),
            Reserved2 = [reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()],
            Reserved3 = reader.ReadUInt32(),
            Section1Offset = reader.ReadUInt32(),
            Section2Offset = reader.ReadUInt32(),
            DataBaseOffset = reader.ReadUInt32(),
            Section3Offset = reader.ReadUInt32(),
            Section4Offset = reader.ReadUInt32(),
        };
    }

    private static void ParseEntryPool(SceneFile rpj, byte[] data)
    {
        var st = rpj.Sections;
        int fileSize = data.Length;
        uint dataBase = st.DataBaseOffset;
        if (dataBase == 0 || dataBase >= (uint)fileSize) return;

        uint relOffset = 0;
        int safetyLimit = 2000;

        while (safetyLimit-- > 0)
        {
            int absOffset = (int)(dataBase + relOffset);
            if (absOffset + EntrySize > fileSize) break;

            rpj.Entries.Add(new Entry
            {
                FileOffset = absOffset,
                RawData = data[absOffset..(absOffset + EntrySize)],
            });

            uint nextEntry = BigEndian.ReadUInt32(data, absOffset + 0x6C);
            if (nextEntry == 0) break;
            relOffset = nextEntry;
        }
    }

    private static void ParseScriptBlocks(SceneFile rpj, byte[] data)
    {
        var st = rpj.Sections;
        int scriptBase = (int)(st.DataBaseOffset + st.Section1Offset);
        int fileSize = data.Length;

        var processedBlockOffsets = new HashSet<int>();

        for (int entryIndex = 0; entryIndex < rpj.Entries.Count; entryIndex++)
        {
            var entry = rpj.Entries[entryIndex];
            uint scriptBlockCount = entry.EntryScriptBlockCount;
            uint scriptBlockOffset = entry.EntryScriptBlockOffset;

            if (scriptBlockCount == 0 || scriptBlockOffset == 0) continue;

            uint blockRelOffset = scriptBlockOffset;
            int blockSafety = 100;

            while (blockSafety-- > 0)
            {
                int blockAbsOffset = scriptBase + (int)blockRelOffset;
                if (blockAbsOffset + 0x100 > fileSize) break;
                if (processedBlockOffsets.Contains(blockAbsOffset)) break;
                processedBlockOffsets.Add(blockAbsOffset);

                byte[] headerData = data[blockAbsOffset..(blockAbsOffset + 0x100)];

                uint bytecodeSize = BigEndian.ReadUInt32(data, blockAbsOffset + 0xEC);
                uint paramDataSize = BigEndian.ReadUInt32(data, blockAbsOffset + 0xF0);
                uint nextBlock = BigEndian.ReadUInt32(data, blockAbsOffset + 0xFC);

                int totalSize = (int)(0x100 + bytecodeSize + paramDataSize);
                if (blockAbsOffset + totalSize > fileSize)
                    totalSize = fileSize - blockAbsOffset;

                var block = new ScriptBlock
                {
                    OwnerEntryIndex = entryIndex,
                    FileOffset = blockAbsOffset,
                    FileSize = totalSize,
                    HeaderData = headerData,
                };

                int bytecodeStart = blockAbsOffset + 0x100;
                int bytecodeEnd = bytecodeStart + (int)bytecodeSize;
                ParseBytecodeRegion(block, data, bytecodeStart, bytecodeEnd);

                int paramStart = bytecodeEnd;
                int paramEnd = paramStart + (int)paramDataSize;
                if (paramEnd <= fileSize && paramDataSize > 0)
                    block.ParamData = data[paramStart..paramEnd];

                rpj.Scripts.Add(block);
                entry.ScriptBlocks.Add(block);

                if (nextBlock == 0) break;
                blockRelOffset = nextBlock;
            }
        }
    }

    private static void ParseBytecodeRegion(ScriptBlock block, byte[] data, int start, int end)
    {
        int pos = start;
        int dataStart = -1;

        void FlushData()
        {
            if (dataStart >= 0 && dataStart < pos)
            {
                block.Elements.Add(new ScriptData { Bytes = data[dataStart..pos] });
                dataStart = -1;
            }
        }

        while (pos < end)
        {
            if (pos + 8 <= end)
            {
                uint opcode = BigEndian.ReadUInt32(data, pos);
                uint size = BigEndian.ReadUInt32(data, pos + 4);

                if (Opcodes.IsValid(opcode) && size >= 8 && size <= 512 && pos + (int)size <= end)
                {
                    FlushData();
                    int paramCount = (int)(size - 8) / 4;
                    var parms = new uint[paramCount];
                    for (int p = 0; p < paramCount; p++)
                        parms[p] = BigEndian.ReadUInt32(data, pos + 8 + p * 4);

                    block.Elements.Add(new ScriptInstruction
                    {
                        Opcode = opcode,
                        Size = size,
                        RawParams = parms,
                    });
                    pos += (int)size;
                    continue;
                }
            }

            if (dataStart < 0) dataStart = pos;
            pos += 4;
        }

        if (dataStart >= 0 && dataStart < end)
        {
            pos = end;
            FlushData();
        }
    }

    private static void ParseWaypoints(SceneFile rpj, byte[] data)
    {
        uint start = rpj.Sections.Section4Offset;
        if (start == 0 || start >= data.Length) return;

        int offset = (int)start;
        while (offset + 0x40 <= data.Length)
        {
            rpj.Waypoints.Add(new Waypoint
            {
                FileOffset = offset,
                RawData = data[offset..(offset + 0x40)],
            });
            offset += 0x40;
        }
    }

    private static void CaptureInterstitialGaps(SceneFile rpj, byte[] data)
    {
        var claimed = new List<(int start, int end)>();
        claimed.Add((0, DataBaseOffset));

        foreach (var n in rpj.Entries)
            claimed.Add((n.FileOffset, n.FileOffset + EntrySize));
        foreach (var s in rpj.Scripts)
            claimed.Add((s.FileOffset, s.FileOffset + s.FileSize));
        foreach (var w in rpj.Waypoints)
            claimed.Add((w.FileOffset, w.FileOffset + 0x40));
        foreach (var g in rpj.Gaps)
            claimed.Add((g.FileOffset, g.FileOffset + g.Data.Length));

        claimed.Sort((a, b) => a.start.CompareTo(b.start));

        var newGaps = new List<RawGap>();
        int cursor = 0;
        foreach (var (gapStart, gapEnd) in claimed)
        {
            if (gapStart > cursor && cursor < data.Length)
            {
                int end = Math.Min(gapStart, data.Length);
                var gapData = data[cursor..end];
                if (gapData.Any(b => b != 0))
                    newGaps.Add(new RawGap { FileOffset = cursor, Data = gapData });
            }
            cursor = Math.Max(cursor, gapEnd);
        }
        if (cursor < data.Length)
        {
            var trailing = data[cursor..];
            if (trailing.Any(b => b != 0))
                newGaps.Add(new RawGap { FileOffset = cursor, Data = trailing });
        }

        rpj.Gaps.AddRange(newGaps);
        rpj.Gaps.Sort((a, b) => a.FileOffset.CompareTo(b.FileOffset));
    }

}
