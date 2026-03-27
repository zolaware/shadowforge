/**
 * @file        Formats/HDB/Reader.cs
 * @brief       HDB binary reader - parses model files into ModelFile
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */
using ShadowForge.IO;

namespace ShadowForge.Formats.HDB;

public static class Reader
{
    public const int HeaderSize = 32;
    public const int FirstTableEntrySize = 16;
    public const int BoneEntrySize = 0x50; // 80 bytes per bone (type 8 in byte-swap)
    public const int TextureEntrySize = 28;

    public static ModelFile Read(string path)
    {
        var data = File.ReadAllBytes(path);
        return Read(data);
    }

    public static ModelFile Read(byte[] data)
    {
        var model = new ModelFile { OriginalFileSize = data.Length };

        // Header
        Array.Copy(data, model.Header.RawData, Math.Min(data.Length, HeaderSize));
        if (model.Header.MagicValue != FileHeader.Magic)
            throw new InvalidDataException($"Invalid HDB magic: 0x{model.Header.MagicValue:X8}");

        // First table
        int ftStart = HeaderSize;
        int ftCount = (int)BigEndian.ReadUInt32(data, ftStart);
        int ftLength = (int)BigEndian.ReadUInt32(data, ftStart + 4);
        int ftEnd = ftStart + ftLength + 4;

        ParseFirstTableEntries(model, data, ftStart, ftCount);

        // Second table
        int stStart = ftEnd;
        if (stStart + 8 <= data.Length)
        {
            model.SecondTable.EntryCount = (int)BigEndian.ReadUInt32(data, stStart);
            model.SecondTable.TableLength = (int)BigEndian.ReadUInt32(data, stStart + 4);

            int entryStart = stStart + 8;
            model.SecondTable.Entries = new int[model.SecondTable.EntryCount];
            for (int i = 0; i < model.SecondTable.EntryCount; i++)
                model.SecondTable.Entries[i] = BigEndian.ReadInt32(data, entryStart + i * 4);

            int stEnd = stStart + model.SecondTable.TableLength + 4;
            if (stEnd < data.Length)
                model.SecondTable.Padding = (int)BigEndian.ReadUInt32(data, stEnd);

            // Preserve raw data region (everything after second table padding to EOF)
            int dataRegionStart = stEnd + 4;
            if (dataRegionStart < data.Length)
            {
                model.DataRegion = new byte[data.Length - dataRegionStart];
                Array.Copy(data, dataRegionStart, model.DataRegion, 0, model.DataRegion.Length);
            }

            // IA and VA regions
            if (model.SecondTable.Entries.Length > 0)
            {
                int lastEntry = model.SecondTable.Entries[^1];
                int iaStart = stEnd - 4 + lastEntry + 16;
                iaStart = (iaStart + 15) & ~15; // 16-byte align

                ReadIndexArrayData(model, data, iaStart);

                int vaStartOffset = stEnd + lastEntry + 12;
                if (vaStartOffset + 4 <= data.Length)
                {
                    int vaStart = iaStart + 16 + BigEndian.ReadInt32(data, vaStartOffset);
                    ReadVertexArrayData(model, data, vaStart);
                }
            }
        }

        // Sort bones by index so callers can rely on list order
        model.Bones.Sort((a, b) => a.Index.CompareTo(b.Index));

        BuildMeshGroups(model);
        return model;
    }

    private static void ParseFirstTableEntries(ModelFile model, byte[] data, int ftStart, int ftCount)
    {
        int entryBase = ftStart + 8; // skip count + length
        for (int i = 0; i < ftCount; i++)
        {
            int entryPos = entryBase + i * FirstTableEntrySize;
            if (entryPos + FirstTableEntrySize > data.Length) break;

            // Preserve raw 16-byte entry for byte-identical round-trip
            var rawEntry = new byte[FirstTableEntrySize];
            Array.Copy(data, entryPos, rawEntry, 0, FirstTableEntrySize);
            model.FirstTableEntries.Add(rawEntry);

            int entryType = (int)BigEndian.ReadUInt32(data, entryPos);
            // skip zero at +4
            int dataOffset = (int)BigEndian.ReadUInt32(data, entryPos + 8);
            int dataLength = (int)BigEndian.ReadUInt32(data, entryPos + 12);

            // Absolute data position: relative to field at entryPos+8
            int absPos = entryPos + 8 + dataOffset;
            if (absPos < 0 || absPos >= data.Length) continue;

            switch (entryType)
            {
                case 7: // Bone
                    ParseBone(model, data, absPos, dataLength);
                    break;
                case 10: // Texture names
                    ParseTextures(model, data, absPos, dataLength);
                    break;
                case 11: // Texture count
                    if (absPos + 4 <= data.Length)
                        model.TextureCount = (int)BigEndian.ReadUInt32(data, absPos);
                    break;
                case 5: // VA setup
                    ParseVaSetup(model, data, absPos);
                    break;
                case 3: // Render commands
                    var cmdData = new byte[dataLength];
                    Array.Copy(data, absPos, cmdData, 0, Math.Min(dataLength, data.Length - absPos));
                    model.RenderCommands.AddRange(RenderCommandStream.Parse(cmdData));
                    break;
                case 4: // IA face count (informational)
                    break;
                case 0: // Padding
                    break;
                default: // Types 6, 9, 12 and any unknown
                    var raw = new byte[dataLength];
                    Array.Copy(data, absPos, raw, 0, Math.Min(dataLength, data.Length - absPos));
                    model.RawEntries.Add(new RawFirstTableEntry
                    {
                        EntryType = entryType,
                        DataOffset = dataOffset,
                        DataLength = dataLength,
                        Data = raw,
                    });
                    break;
            }
        }
    }

    private static void ParseBone(ModelFile model, byte[] data, int pos, int length)
    {
        var bone = new Bone();
        bone.RawData = new byte[length];
        Array.Copy(data, pos, bone.RawData, 0, Math.Min(length, data.Length - pos));

        bone.Index = (int)BigEndian.ReadUInt32(data, pos + 0);
        bone.HFlag = (int)BigEndian.ReadUInt32(data, pos + 8);
        bone.PosX = BigEndian.ReadFloat(data, pos + 16);
        bone.PosY = BigEndian.ReadFloat(data, pos + 20);
        bone.PosZ = BigEndian.ReadFloat(data, pos + 24);
        bone.EulerX = BigEndian.ReadFloat(data, pos + 28);
        bone.EulerY = BigEndian.ReadFloat(data, pos + 32);
        bone.EulerZ = BigEndian.ReadFloat(data, pos + 36);
        bone.ScaleX = BigEndian.ReadFloat(data, pos + 44);
        bone.ScaleY = BigEndian.ReadFloat(data, pos + 48);
        bone.ScaleZ = BigEndian.ReadFloat(data, pos + 52);

        // Child/parent pointers (relative)
        int childPtr = BigEndian.ReadInt32(data, pos + 56);
        int parentPtr = BigEndian.ReadInt32(data, pos + 60);
        bone.ChildIndex = childPtr != 0 ? (int)BigEndian.ReadUInt32(data, pos + 56 + childPtr) : -1;
        bone.ParentIndex = parentPtr != 0 ? (int)BigEndian.ReadUInt32(data, pos + 60 + parentPtr) : -1;

        // Name (16 bytes ASCII at offset 64)
        bone.Name = System.Text.Encoding.ASCII.GetString(data, pos + 64, 16).TrimEnd('\0');

        // Extra euler (6 floats at offset 80)
        bone.ExtraEuler = new float[6];
        for (int j = 0; j < 6; j++)
            bone.ExtraEuler[j] = BigEndian.ReadFloat(data, pos + 80 + j * 4);

        model.Bones.Add(bone);
    }

    private static void ParseTextures(ModelFile model, byte[] data, int pos, int dataLength)
    {
        int count = dataLength / TextureEntrySize;
        for (int j = 0; j < count; j++)
        {
            int off = pos + j * TextureEntrySize;
            var entry = new TextureEntry();
            Array.Copy(data, off, entry.RawData, 0, Math.Min(TextureEntrySize, data.Length - off));
            entry.Name = System.Text.Encoding.ASCII.GetString(data, off, 16).TrimEnd('\0');
            model.Textures.Add(entry);
        }
    }

    private static void ParseVaSetup(ModelFile model, byte[] data, int pos)
    {
        int numVas = (int)BigEndian.ReadUInt32(data, pos);
        int cur = pos + 4;
        for (int j = 0; j < numVas; j++)
        {
            var va = new VertexArray
            {
                VaOffset = (int)BigEndian.ReadUInt32(data, cur),
                VaSize = (int)BigEndian.ReadUInt32(data, cur + 4),
                VaType = BigEndian.ReadUInt32(data, cur + 8),
            };
            model.VertexArrays.Insert(0, va); // reverse order per Python parser
            cur += 12;
        }
    }

    private static void ReadIndexArrayData(ModelFile model, byte[] data, int iaStart)
    {
        // IA data read is driven by render commands that specify size/start
        foreach (var cmd in model.RenderCommands)
        {
            if (cmd.Opcode != 0x10 && cmd.Opcode != 0x20 && cmd.Opcode != 0x30) continue;
            if (cmd.Data.Length < 5) continue;

            int iaSize = 2 + BigEndian.ReadUInt16(cmd.Data, 1);
            int iaStartOff = BigEndian.ReadUInt16(cmd.Data, 3);

            var ia = new IndexArray
            {
                Size = iaSize,
                Start = iaStartOff,
                Type = cmd.Opcode,
            };

            int byteOff = iaStart + iaStartOff * 2;
            ia.Indices = new ushort[iaSize];
            for (int k = 0; k < iaSize && byteOff + 2 <= data.Length; k++)
            {
                ia.Indices[k] = BigEndian.ReadUInt16(data, byteOff);
                byteOff += 2;
            }

            model.IndexArrays.Add(ia);
        }
    }

    private static void ReadVertexArrayData(ModelFile model, byte[] data, int vaStart)
    {
        foreach (var va in model.VertexArrays)
        {
            int offset = vaStart + va.VaOffset;
            int size = va.VaSize;
            if (offset + size > data.Length) size = data.Length - offset;
            if (size <= 0) continue;
            va.RawVertices = new byte[size];
            Array.Copy(data, offset, va.RawVertices, 0, size);
        }
    }

    private static void BuildMeshGroups(ModelFile model)
    {
        int currentVa = -1;
        int currentMaterial = -1;
        var currentPalette = new List<ushort>();
        int iaIndex = 0;

        foreach (var cmd in model.RenderCommands)
        {
            switch (cmd.Opcode)
            {
                case 0x40: // VA Select
                    if (cmd.Data.Length >= 3)
                        currentVa = BigEndian.ReadUInt16(cmd.Data, 1);
                    break;
                case 0x60: // Material Select
                    if (cmd.Data.Length >= 1)
                        currentMaterial = cmd.Data[0];
                    break;
                case 0x02: // Matrix Palette
                    currentPalette = new List<ushort>();
                    if (cmd.Data.Length >= 1)
                    {
                        int count = cmd.Data[0];
                        for (int i = 0; i < count && 1 + i * 2 + 2 <= cmd.Data.Length; i++)
                            currentPalette.Add(BigEndian.ReadUInt16(cmd.Data, 1 + i * 2));
                    }
                    model.MatrixPalettes.Add(currentPalette);
                    break;
                case 0x10: case 0x20: case 0x30: // IA Select
                    if (iaIndex < model.IndexArrays.Count)
                    {
                        model.IndexArrays[iaIndex].MaterialIndex = currentMaterial;
                        model.MeshGroups.Add(new MeshGroup
                        {
                            VaIndex = currentVa,
                            IaIndex = iaIndex,
                            MaterialIndex = currentMaterial,
                            Topology = cmd.Opcode,
                            BonePalette = new List<ushort>(currentPalette),
                        });
                        iaIndex++;
                    }
                    break;
            }
        }
    }
}
