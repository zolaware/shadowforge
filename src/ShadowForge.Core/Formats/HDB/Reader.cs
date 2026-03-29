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
        Logger.Debug($"Header processed");
        // First table
        int ftStart = HeaderSize;
        int ftCount = (int)BigEndian.ReadUInt32(data, ftStart);
        int ftLength = (int)BigEndian.ReadUInt32(data, ftStart + 4);
        int ftEnd = ftStart + ftLength + 4;
        Logger.Debug($"First Table Header processed");
        Logger.Debug($"Processing First Table Entries...");
        ParseFirstTableEntries(model, data, ftStart, ftCount);
        Logger.Debug($"First Table Entries processed");

        Logger.Debug($"Processing Second Table Entries...");
        // Second table
        int stStart = ftEnd;
        if (stStart + 8 <= data.Length)
        {
            model.SecondTable.EntryCount = (int)BigEndian.ReadUInt32(data, stStart);
            model.SecondTable.TableLength = (int)BigEndian.ReadUInt32(data, stStart + 4);
            Logger.Debug($"SeconD Table Entries count: {model.SecondTable.EntryCount}");
            Logger.Debug($"SeconD Table Entries length: {model.SecondTable.TableLength}");

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
                Logger.Debug($"-- Second Table End: {stEnd}");
                iaStart = (iaStart + 15) & ~15; // 16-byte align
                Logger.Debug($"-- IA Start: {iaStart}");

                // Traverse the IA data and return the dynamically calculated true VA start
                int trueVaStart = ReadIndexArrayData(model, data, iaStart);

                if (trueVaStart <= data.Length)
                {
                    Logger.Debug($"-- VA Start: {trueVaStart}");
                    ReadVertexArrayData(model, data, trueVaStart);
                }
            }
        }

        // Sort bones by index so callers can rely on list order
        model.Bones.Sort((a, b) => a.Index.CompareTo(b.Index));

        // Insert dummy bones if an expected index is missing (matches Python's safe array generation)
        if (model.Bones.Count > 0)
        {
            int maxIndex = model.Bones[^1].Index;
            for (int i = 0; i < maxIndex; i++)
            {
                if (model.Bones[i].Index != i)
                {
                    model.Bones.Insert(i, new Bone
                    {
                        Index = i,
                        HFlag = 0,
                        PosX = 0f,
                        PosY = 0f,
                        PosZ = 0f,
                        EulerX = 0f,
                        EulerY = 0f,
                        EulerZ = 0f,
                        ScaleX = 0f,
                        ScaleY = 0f,
                        ScaleZ = 0f, // Python zeroes these out for dummies
                        ChildIndex = -1,
                        ParentIndex = -1,
                        Name = "Dummy",
                        ExtraEuler = new float[6]
                    });
                }
            }
        }

        // Derive parent relationships from child pointers rather than the parent
        // field. The parent pointer encodes a different relationship; the Python
        // parser's get_armature() builds the hierarchy by following child pointers.
        DeriveParentsFromChildren(model);

        BuildMeshGroups(model);
        return model;
    }

    private static void ParseFirstTableEntries(ModelFile model, byte[] data, int ftStart, int ftCount)
    {
        Logger.Debug($"First Table Entries Reading Start");

        int entryBase = ftStart + 8; // skip count + length
        bool needsBoneInjection = false; // Tracks if a bone needs to be injected into an empty palette

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

                    // --- Empty Matrix Palette Bone Injection ---
                    if (needsBoneInjection)
                    {
                        ushort boneIdx = (ushort)model.Bones[^1].Index;

                        // Replicate Python's pointer reference behavior:
                        // Find all empty palettes first, then append to them. 
                        // If there are duplicate references, it appends multiple times (e.g. [79, 79])
                        var emptyPalettes = model.MatrixPalettes.Where(p => p.Count == 0).ToList();
                        foreach (var pal in emptyPalettes)
                        {
                            pal.Add(boneIdx);
                        }

                        needsBoneInjection = false;
                    }
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
                    var parsedCommands = RenderCommandStream.Parse(cmdData);

                    // Insert at 0 to prepend the chunk
                    model.RenderCommands.InsertRange(0, parsedCommands);

                    // --- Evaluate Matrix Palettes immediately to sync with Bone Injection ---
                    var chunkMp = new List<List<ushort>>();
                    var lastMp = new List<ushort>();
                    bool vaCheck = false;
                    bool mpCheck = false;

                    foreach (var cmd in parsedCommands)
                    {
                        if (cmd.Opcode == 0x40) // VA Select
                        {
                            vaCheck = true;
                            mpCheck = false;
                        }
                        else if (cmd.Opcode == 0x02) // Matrix Palette
                        {
                            var pal = new List<ushort>();
                            if (cmd.Data != null && cmd.Data.Length >= 1)
                            {
                                int count = cmd.Data[0];
                                for (int k = 0; k < count && 1 + k * 2 + 2 <= cmd.Data.Length; k++)
                                    pal.Add(BigEndian.ReadUInt16(cmd.Data, 1 + k * 2));
                            }
                            chunkMp.Add(pal);
                            lastMp = pal; // Passed by reference! Matches Python's pointer behavior.
                            mpCheck = true;

                            if (pal.Count == 0) needsBoneInjection = true;
                        }
                        else if (cmd.Opcode == 0x10 || cmd.Opcode == 0x20 || cmd.Opcode == 0x30) // IA Select
                        {
                            // Python's duplication fallback
                            if (vaCheck != mpCheck)
                            {
                                chunkMp.Add(lastMp); // Appends the reference to replicate Python

                                // Ensure we trigger bone injection if the fallback palette is empty!
                                if (lastMp.Count == 0)
                                {
                                    needsBoneInjection = true;
                                }

                                vaCheck = false;
                                mpCheck = false;
                            }
                        }
                    }

                    // Prepend this chunk's evaluated palettes to the global list
                    model.MatrixPalettes.InsertRange(0, chunkMp);
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
        Logger.Debug($"---Bone created with Index: {bone.Index} - '{bone.Name}'");
    }

    /// <summary>
    /// Ports the Python parser's get_armature + get_parent to derive the correct
    /// skeleton hierarchy. The child pointer gives first-child, the parent field
    /// enables backtracking to find siblings. Each bone's ParentIndex is set to
    /// the armature-derived parent (a_parent).
    /// </summary>
    private static void DeriveParentsFromChildren(ModelFile model)
    {
        var bones = model.Bones;
        if (bones.Count == 0) return;

        // Create a dictionary to safely look up bones by their Absolute Index
        var boneMap = bones.ToDictionary(b => b.Index);

        var armature = new List<List<int>>();
        var remainingBones = bones.ToList();
        int rowIndex = 1;

        while (remainingBones.Count > 0)
        {
            var currentBone = remainingBones[0];
            var currentRow = new List<int>();
            bool done = false;

            while (!done)
            {
                // get_row equivalent
                bool doneRow = false;
                while (!doneRow)
                {
                    rowIndex++;
                    currentRow.Add(currentBone.Index);

                    if (currentBone.ChildIndex == -1)
                    {
                        doneRow = true;
                    }
                    else
                    {
                        if (boneMap.TryGetValue(currentBone.ChildIndex, out var childBone))
                            currentBone = childBone;
                        else
                            doneRow = true; // Fallback for broken pointers
                    }
                }

                armature.Add(new List<int>(currentRow));

                // Remove bones in the current row from remaining bones
                remainingBones.RemoveAll(b => currentRow.Contains(b.Index));

                if (currentBone.ParentIndex != -1)
                {
                    if (boneMap.TryGetValue(currentRow[^1], out var lastRowBone) &&
                        lastRowBone.ParentIndex != -1 &&
                        boneMap.TryGetValue(lastRowBone.ParentIndex, out var parentBone))
                    {
                        currentBone = parentBone;
                        currentRow.RemoveAt(currentRow.Count - 1);
                    }
                    else
                    {
                        // Fallback if lookup fails
                        currentRow.RemoveAt(currentRow.Count - 1);
                    }
                }
                else
                {
                    if (currentRow.Count > 1)
                    {
                        bool foundValidParent = false;
                        bool reachedMinimum = false;

                        while (!foundValidParent)
                        {
                            currentRow.RemoveAt(currentRow.Count - 1);
                            if (!boneMap.TryGetValue(currentRow[^1], out currentBone))
                                break;

                            if (currentBone.ParentIndex != -1)
                            {
                                currentRow.RemoveAt(currentRow.Count - 1);
                                if (boneMap.TryGetValue(currentBone.ParentIndex, out var parentBone))
                                {
                                    currentBone = parentBone;
                                    foundValidParent = true;
                                }
                            }
                            else if (currentRow.Count == 1)
                            {
                                reachedMinimum = true;
                                foundValidParent = true;
                            }
                        }

                        if (reachedMinimum)
                        {
                            done = true;
                        }
                    }
                    else
                    {
                        remainingBones.RemoveAll(b => currentRow.Contains(b.Index));
                        if (rowIndex < bones.Count)
                        {
                            done = true;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
        }

        Logger.Debug($"Armature: {string.Join(", ", armature.Select(a => "[" + string.Join(",", a) + "]"))}");

        // Derive ParentIndex: for each bone, find the FIRST armature row containing it
        // and set parent to the bone before it in that row.
        foreach (var bone in bones)
        {
            int derivedParent = -1;
            foreach (var row in armature)
            {
                int idx = row.IndexOf(bone.Index);
                if (idx != -1)
                {
                    derivedParent = idx > 0 ? row[idx - 1] : -1;
                    break;
                }
            }
            bone.ParentIndex = derivedParent;
        }
    }

    private static void ParseTextures(ModelFile model, byte[] data, int pos, int dataLength)
    {
        int count = dataLength / TextureEntrySize;
        Logger.Debug($"Texture count: {count}");
        for (int j = 0; j < count; j++)
        {
            int off = pos + j * TextureEntrySize;
            var entry = new TextureEntry();
            Array.Copy(data, off, entry.RawData, 0, Math.Min(TextureEntrySize, data.Length - off));
            entry.Name = System.Text.Encoding.ASCII.GetString(data, off, 16).TrimEnd('\0');
            model.Textures.Add(entry);
            Logger.Debug($"Texture Name - {entry.Name}");
        }
    }

    private static void ParseVaSetup(ModelFile model, byte[] data, int pos)
    {
        int numVas = (int)BigEndian.ReadUInt32(data, pos);
        int cur = pos + 4;
        Logger.Debug($"Entry type 5 VA Setup Entry detected!");
        Logger.Debug($"--- Num of VAs: {numVas}");

        var chunkVas = new List<VertexArray>();
        for (int j = 0; j < numVas; j++)
        {
            var va = new VertexArray
            {
                VertexCount = (int)BigEndian.ReadUInt32(data, cur),
                VaType = BigEndian.ReadUInt32(data, cur + 4),
                VaOffset = (int)BigEndian.ReadUInt32(data, cur + 8),
            };
            chunkVas.Add(va);
            Logger.Debug($"--- VAs: {va.VertexCount}, {va.VaType}, {va.VaOffset}");
            cur += 12;
        }

        // Fix: Do NOT reverse the chunk internally. Insert it at the beginning 
        // to perfectly match the prepend order of the Render Commands.
        model.VertexArrays.InsertRange(0, chunkVas);
    }

    private static int ReadIndexArrayData(ModelFile model, byte[] data, int iaStart)
    {
        int pos = iaStart;

        // Replicate Python's zero-checks for pointer adjustment
        if (pos + 4 <= data.Length)
        {
            uint val1 = BigEndian.ReadUInt32(data, pos);
            if (val1 == 0) pos += 16;
            else if (pos + 8 <= data.Length && BigEndian.ReadUInt32(data, pos + 4) == 0) pos += 16;
        }

        // Determine how many IA chunks exist by recounting the Type 3 entries
        var iaCounts = new List<int>();
        int ftStart = HeaderSize;
        int entryBase = ftStart + 8;
        int ftCount = (int)BigEndian.ReadUInt32(data, ftStart);

        for (int i = 0; i < ftCount; i++)
        {
            int entryPos = entryBase + i * FirstTableEntrySize;
            if (entryPos + FirstTableEntrySize > data.Length) break;

            if (BigEndian.ReadUInt32(data, entryPos) == 3) // Type 3: Render Commands
            {
                int dataOffset = (int)BigEndian.ReadUInt32(data, entryPos + 8);
                int dataLength = (int)BigEndian.ReadUInt32(data, entryPos + 12);
                int absPos = entryPos + 8 + dataOffset;

                var cmdData = new byte[dataLength];
                Array.Copy(data, absPos, cmdData, 0, Math.Min(dataLength, data.Length - absPos));
                var parsedCommands = RenderCommandStream.Parse(cmdData);

                int count = 0;
                foreach (var cmd in parsedCommands)
                {
                    if (cmd.Opcode == 0x10 || cmd.Opcode == 0x20 || cmd.Opcode == 0x30) count++;
                }
                iaCounts.Insert(0, count); // Prepend to match Python's Tot_IA_Sizes order
            }
        }

        int cmdIndex = 0;
        var iaCommands = model.RenderCommands.Where(c => c.Opcode == 0x10 || c.Opcode == 0x20 || c.Opcode == 0x30).ToList();

        // Traverse the chunks
        for (int w = 0; w < iaCounts.Count; w++)
        {
            if (pos + 16 > data.Length) break;

            int iaCheck = (int)BigEndian.ReadUInt32(data, pos);
            int blockBodyStart = pos + 16;
            pos += 16;

            int countForChunk = iaCounts[w];
            for (int i = 0; i < countForChunk; i++)
            {
                if (cmdIndex >= iaCommands.Count) break;
                var cmd = iaCommands[cmdIndex++];

                int iaSize = 2 + BigEndian.ReadUInt16(cmd.Data, 1);
                int iaStartOff = BigEndian.ReadUInt16(cmd.Data, 3);

                var ia = new IndexArray
                {
                    Size = iaSize,
                    Start = iaStartOff,
                    Type = cmd.Opcode,
                };

                // Advance pointer to the start offset for this specific index array
                int posCheck = (pos - blockBodyStart) / 2;
                if (iaStartOff > posCheck)
                {
                    pos += 2 * (iaStartOff - posCheck);
                }

                ia.Indices = new ushort[iaSize];
                for (int k = 0; k < iaSize && pos + 2 <= data.Length; k++)
                {
                    ia.Indices[k] = BigEndian.ReadUInt16(data, pos);
                    pos += 2;
                }

                model.IndexArrays.Add(ia);
            }

            // Skip any remaining padding bytes in this specific chunk
            int sPosCheck = pos - blockBodyStart;
            if (iaCheck > sPosCheck)
            {
                pos += (iaCheck - sPosCheck);
            }
        }

        // The final pointer position is the true start of the Vertex Arrays
        return pos;
    }

    private static void ReadVertexArrayData(ModelFile model, byte[] data, int vaStart)
    {
        // Read VA data sequentially from the data region. Each VA has a 16-byte
        // header [byte_size:u32, format_type:u32, vertex_count:u32, zero:u32]
        // followed by byte_size bytes of vertex data.
        int pos = vaStart;
        Logger.Debug($"VA region starting at file pointer {vaStart}");
        foreach (var va in model.VertexArrays)
        {
            if (pos + 16 > data.Length) break;

            int byteSize = (int)BigEndian.ReadUInt32(data, pos);
            uint formatType = BigEndian.ReadUInt32(data, pos + 4);
            int vertexCount = (int)BigEndian.ReadUInt32(data, pos + 8);

            va.VaType = formatType;
            va.VertexCount = vertexCount;
            va.VaSize = byteSize;
            va.VaOffset = pos - vaStart;

            int vertexDataStart = pos + 16;
            int available = Math.Min(byteSize, data.Length - vertexDataStart);
            if (available > 0)
            {
                va.RawVertices = new byte[available];
                Array.Copy(data, vertexDataStart, va.RawVertices, 0, available);
            }

            pos = vertexDataStart + byteSize;
        }
    }

    private static void BuildMeshGroups(ModelFile model)
    {
        int currentVa = -1;
        int currentMaterial = -1;
        int iaIndex = 0;
        int mpIndex = 0; // Track sequential palette index

        bool vaCheck = false;
        bool mpCheck = false;

        // Variables to handle local -> global VA mapping across chunks
        int globalVaOffset = 0;
        int maxVaInChunk = -1;

        foreach (var cmd in model.RenderCommands)
        {
            switch (cmd.Opcode)
            {
                case 0x00: // End or zero padding
                    // 0xFF marks the end of a render commands chunk
                    if (cmd.Data != null && cmd.Data.Length > 0 && cmd.Data[0] == 0xFF)
                    {
                        if (maxVaInChunk >= 0)
                        {
                            // Advance the global offset by the amount of VAs in the chunk we just finished
                            globalVaOffset += (maxVaInChunk + 1);
                            maxVaInChunk = -1;
                        }
                    }
                    break;

                case 0x40: // VA Select
                    if (cmd.Data.Length >= 3)
                    {
                        currentVa = BigEndian.ReadUInt16(cmd.Data, 1);
                        // Track the highest local VA index used in this chunk
                        if (currentVa > maxVaInChunk)
                            maxVaInChunk = currentVa;
                    }

                    vaCheck = true;
                    mpCheck = false;
                    break;

                case 0x60: // Material Select
                    if (cmd.Data.Length >= 1)
                        currentMaterial = cmd.Data[0];
                    break;

                case 0x02: // Matrix Palette
                    mpCheck = true;
                    Logger.Debug($"% VA Num {mpIndex}");
                    Logger.Debug($"Current Palette: [{string.Join(", ", model.MatrixPalettes[mpIndex])}]");
                    mpIndex++;
                    break;

                case 0x10:
                case 0x20:
                case 0x30: // IA Select
                    if (vaCheck != mpCheck)
                    {
                        Logger.Debug("Empty MP detected in IA Selection, defaulting to previous.");
                        Logger.Debug($"% VA Num {mpIndex}");
                        Logger.Debug($"Current Palette: [{string.Join(", ", model.MatrixPalettes[mpIndex])}]");
                        mpIndex++;

                        vaCheck = false;
                        mpCheck = false;
                    }

                    if (iaIndex < model.IndexArrays.Count)
                    {
                        model.IndexArrays[iaIndex].MaterialIndex = currentMaterial;
                        model.MeshGroups.Add(new MeshGroup
                        {
                            // Fix: Combine the chunk's global offset with the file's local VA index
                            VaIndex = globalVaOffset + currentVa,
                            IaIndex = iaIndex,
                            MaterialIndex = currentMaterial,
                            Topology = cmd.Opcode,
                            BonePalette = new List<ushort>(model.MatrixPalettes[mpIndex - 1])
                        });
                        iaIndex++;
                    }
                    break;
            }
        }
    }
}
