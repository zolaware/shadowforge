/**
 * @file        Formats/HDB/Writer.cs
 * @brief       HDB binary writer - serializes ModelFile to binary
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */
using ShadowForge.IO;

namespace ShadowForge.Formats.HDB;

public static class Writer
{
    public static byte[] Write(ModelFile model)
    {
        using var ms = new MemoryStream();

        // Header (32 bytes)
        ms.Write(model.Header.RawData);

        // First table header: count + length
        int ftCount = model.FirstTableEntries.Count;
        int ftLength = 4 + ftCount * Reader.FirstTableEntrySize;
        WriteBE32(ms, (uint)ftCount);
        WriteBE32(ms, (uint)ftLength);

        // First table entries (raw 16-byte records in original order)
        foreach (var entry in model.FirstTableEntries)
            ms.Write(entry);

        // Second table header: count + length
        WriteBE32(ms, (uint)model.SecondTable.EntryCount);
        WriteBE32(ms, (uint)model.SecondTable.TableLength);

        // Second table entries
        foreach (var e in model.SecondTable.Entries)
            WriteBE32(ms, (uint)e);

        // Second table padding value
        WriteBE32(ms, (uint)model.SecondTable.Padding);

        // Data region (entry payloads + IA + VA data to EOF)
        ms.Write(model.DataRegion);

        return ms.ToArray();
    }

    public static void Write(ModelFile model, string path)
        => File.WriteAllBytes(path, Write(model));

    private static void WriteBE32(Stream s, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        s.Write(buf);
    }
}
