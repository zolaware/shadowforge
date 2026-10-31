using ShadowForge.Formats.HDB.Raw;
using ShadowForge.IO;

namespace ShadowForge.Formats.HDB;

/// <summary>
/// Builds a ModelFile from a RawModel: parent links, index ranges, matrix palettes and
/// draws. Type-3 chunks are walked in reverse disk order, because chunk k from the end
/// pairs with index block k.
/// </summary>
public static class ModelCooker
{
    public static ModelFile Bake(RawModel raw)
    {
        var model = new ModelFile { Header = raw.Header };
        var chunks = raw.FirstTable.OfType<RawRenderCommandEntry>().Reverse().ToList();

        BakeBones(raw, model);
        BakeTextures(raw, model);
        BakeVertexArrays(raw, model);
        BakeIndexArrays(raw, chunks, model);
        var matrixPalettes = BakeMatrixPalettes(raw, chunks);
        foreach (var chunk in chunks)
            model.RenderCommandChunks.Add(new List<RenderCommand>(chunk.Commands));
        BuildMeshGroups(model, matrixPalettes);

        model.SecondTable = new SecondTableData
        {
            Entries = (int[])raw.SecondTable.Entries.Clone(),
            Padding = raw.SecondTable.Padding,
        };
        return model;
    }

    /// <summary>
    /// Cooked bones are sorted by Index. Gaps in the Index range get zero-scale
    /// placeholders named "Dummy", so a bone's list position equals its Index.
    /// </summary>
    private static void BakeBones(RawModel raw, ModelFile model)
    {
        var layout = BoneLayout.Build(raw);
        var parentByBoneIndex = DeriveParents(layout);

        foreach (var e in layout.Bones)
        {
            model.Bones.Add(new Bone
            {
                Index = (int)e.Index,
                HFlag = (int)e.HFlag,
                PosX = e.Position.X,
                PosY = e.Position.Y,
                PosZ = e.Position.Z,
                EulerX = e.Euler.X,
                EulerY = e.Euler.Y,
                EulerZ = e.Euler.Z,
                ScaleX = e.Scale.X,
                ScaleY = e.Scale.Y,
                ScaleZ = e.Scale.Z,
                ExtraEuler = (float[])e.ExtraEuler.Clone(),
                Name = e.Name,
                ChildIndex = e.ChildIndex,
                ParentIndex = parentByBoneIndex.TryGetValue((int)e.Index, out var pi) ? pi : -1,
            });
        }

        model.Bones.Sort((a, b) => a.Index.CompareTo(b.Index));
        if (model.Bones.Count == 0) return;

        int maxIndex = model.Bones[^1].Index;
        for (int i = 0; i < maxIndex; i++)
        {
            if (model.Bones[i].Index != i)
            {
                model.Bones.Insert(i, new Bone
                {
                    Index = i,
                    Name = "Dummy",
                    ScaleX = 0f,
                    ScaleY = 0f,
                    ScaleZ = 0f,
                });
            }
        }
    }

    /// <summary>
    /// Pre-order walk of the first-child / next-sibling tree from its root bone.
    /// Returns parent Index by bone Index, -1 for roots. Bones the walk does not
    /// reach are left out.
    /// </summary>
    private static Dictionary<int, int> DeriveParents(BoneLayout layout)
    {
        var result = new Dictionary<int, int>(layout.Bones.Count);
        if (layout.Bones.Count == 0) return result;

        var stack = new Stack<(int Ft, int ParentFt)>();
        stack.Push((FindRootFt(layout), -1));
        while (stack.Count > 0)
        {
            var (ft, parentFt) = stack.Pop();
            var bone = layout.Bones[ft];
            result[(int)bone.Index] = parentFt < 0 ? -1 : (int)layout.Bones[parentFt].Index;

            if (bone.NextSiblingFtPos >= 0)
                stack.Push((bone.NextSiblingFtPos, parentFt));
            if (bone.ChildFtPos >= 0)
                stack.Push((bone.ChildFtPos, ft));
        }
        return result;
    }

    /// <summary>
    /// FT position of the bone no other bone names as its child or next sibling. Retail
    /// files store it last and importer output stores it first. Falls back to the last
    /// bone when there is no single such bone.
    /// </summary>
    private static int FindRootFt(BoneLayout layout)
    {
        var referenced = new bool[layout.Bones.Count];
        foreach (var bone in layout.Bones)
        {
            if (bone.ChildFtPos >= 0 && bone.ChildFtPos < referenced.Length) referenced[bone.ChildFtPos] = true;
            if (bone.NextSiblingFtPos >= 0 && bone.NextSiblingFtPos < referenced.Length) referenced[bone.NextSiblingFtPos] = true;
        }

        var roots = Enumerable.Range(0, referenced.Length).Where(ft => !referenced[ft]).ToList();
        return roots.Count == 1 ? roots[0] : layout.Bones.Count - 1;
    }

    private static void BakeTextures(RawModel raw, ModelFile model)
    {
        foreach (var tt in raw.FirstTable.OfType<RawTextureTableEntry>())
            foreach (var rec in tt.Records)
                model.Textures.Add(new TextureEntry { Name = rec.Name });

        var texCount = raw.FirstTable.OfType<RawTextureCountEntry>().FirstOrDefault();
        model.TextureCount = texCount != null ? (int)texCount.Count : model.Textures.Count;
    }

    private static void BakeVertexArrays(RawModel raw, ModelFile model)
    {
        foreach (var va in raw.VertexArrays)
        {
            model.VertexArrays.Add(new VertexArray
            {
                VAType = va.FormatType,
                VertexCount = (int)va.VertexCount,
                VASize = (int)va.ByteSize,
                RawVertices = va.RawBytes,
            });
        }
    }

    /// <summary>
    /// One IndexArray per IA-select, sliced from the chunk's index block. Ranges past the
    /// end of the block are skipped.
    /// </summary>
    private static void BakeIndexArrays(RawModel raw, List<RawRenderCommandEntry> chunks, ModelFile model)
    {
        for (int k = 0; k < chunks.Count; k++)
        {
            int blockIdx = raw.IndexBlocks.Count - chunks.Count + k;
            if (blockIdx < 0) continue;
            var block = raw.IndexBlocks[blockIdx];

            foreach (var cmd in chunks[k].Commands)
            {
                if (!RenderCommandStream.IsIndexSelect(cmd.Opcode) || cmd.Data.Length < 5) continue;

                int size = 2 + BigEndian.ReadUInt16(cmd.Data, 1);
                int start = BigEndian.ReadUInt16(cmd.Data, 3);
                if (start + size > block.Indices.Length) continue;

                model.IndexArrays.Add(new IndexArray
                {
                    Start = start,
                    Indices = block.Indices[start..(start + size)],
                });
            }
        }
    }

    /// <summary>
    /// Palettes in the order BuildMeshGroups consumes them. Each 0x02 command adds a
    /// palette of its declared length. An IA-select for which
    /// <see cref="PaletteTracker.Step"/> returns true adds the previous palette again. An
    /// empty palette gets the Index of the first bone entry after its type-3 entry in disk
    /// order, when there is one. The tracker restarts per chunk here but not in
    /// BuildMeshGroups.
    /// </summary>
    private static List<List<ushort>> BakeMatrixPalettes(RawModel raw, List<RawRenderCommandEntry> chunks)
    {
        var injectionByChunk = new Dictionary<RawRenderCommandEntry, ushort>();
        var pending = new List<RawRenderCommandEntry>();
        foreach (var entry in raw.FirstTable)
        {
            if (entry is RawRenderCommandEntry rc)
            {
                pending.Add(rc);
            }
            else if (entry is RawBoneEntry bone)
            {
                foreach (var p in pending)
                    injectionByChunk[p] = (ushort)bone.Index;
                pending.Clear();
            }
        }

        var matrixPalettes = new List<List<ushort>>();
        foreach (var chunk in chunks)
        {
            ushort? injection = injectionByChunk.TryGetValue(chunk, out var b) ? b : null;
            var lastPalette = new List<ushort>();
            var tracker = new PaletteTracker();

            foreach (var cmd in chunk.Commands)
            {
                if (cmd.Opcode == 0x02)
                {
                    var palette = new List<ushort>();
                    if (cmd.Data.Length >= 1)
                    {
                        int count = cmd.Data[0];
                        for (int k = 0; k < count && 3 + k * 2 <= cmd.Data.Length; k++)
                            palette.Add(BigEndian.ReadUInt16(cmd.Data, 1 + k * 2));
                    }
                    if (palette.Count == 0 && injection.HasValue) palette.Add(injection.Value);
                    matrixPalettes.Add(palette);
                    lastPalette = palette;
                }
                else if (tracker.Step(cmd.Opcode))
                {
                    if (lastPalette.Count == 0 && injection.HasValue) lastPalette.Add(injection.Value);
                    matrixPalettes.Add(lastPalette);
                }
                tracker.Observe(cmd.Opcode);
            }
        }
        return matrixPalettes;
    }

    /// <summary>
    /// Emits one MeshGroup per IA-select. The palette cursor advances in step with
    /// BakeMatrixPalettes. VA indices are chunk-relative, so the VA offset moves past the
    /// highest VA index of each chunk at the chunk boundary. Stage words (0x61, 0x62) stay
    /// bound until the next 0x60. Internal so tests can drive it with a hand-built model.
    /// </summary>
    internal static void BuildMeshGroups(ModelFile model, List<List<ushort>> matrixPalettes)
    {
        int currentVA = -1;
        int currentMaterial = -1;
        int currentStage1 = -1;
        int currentStage2 = -1;
        int iaIndex = 0;
        int paletteCount = 0;
        int vaOffset = 0;
        var tracker = new PaletteTracker();

        foreach (var chunk in model.RenderCommandChunks)
        {
            int maxVAInChunk = -1;
            foreach (var cmd in chunk)
            {
                switch (cmd.Opcode)
                {
                    case 0x40 when cmd.Data.Length >= 3:
                        currentVA = BigEndian.ReadUInt16(cmd.Data, 1);
                        maxVAInChunk = Math.Max(maxVAInChunk, currentVA);
                        break;
                    case 0x60:
                        if (cmd.Data.Length >= 1) currentMaterial = cmd.Data[0];
                        currentStage1 = -1;
                        currentStage2 = -1;
                        break;
                    case 0x61 when cmd.Data.Length >= 1:
                        currentStage1 = cmd.Data[0];
                        break;
                    case 0x62 when cmd.Data.Length >= 1:
                        currentStage2 = cmd.Data[0];
                        break;
                    case 0x02:
                        paletteCount++;
                        break;
                }

                if (tracker.Step(cmd.Opcode))
                    paletteCount++;

                if (RenderCommandStream.IsIndexSelect(cmd.Opcode)
                    && iaIndex < model.IndexArrays.Count && paletteCount >= 1 && paletteCount - 1 < matrixPalettes.Count)
                {
                    var ia = model.IndexArrays[iaIndex];
                    ia.MaterialIndex = currentMaterial;
                    ia.Stage1TexIndex = currentStage1;
                    ia.Stage2TexIndex = currentStage2;
                    model.MeshGroups.Add(new MeshGroup
                    {
                        VAIndex = vaOffset + currentVA,
                        IAIndex = iaIndex,
                        MaterialIndex = currentMaterial,
                        Topology = cmd.Opcode,
                        BonePalette = new List<ushort>(matrixPalettes[paletteCount - 1]),
                        Stage1TexIndex = currentStage1,
                        Stage2TexIndex = currentStage2,
                    });
                    iaIndex++;
                }
                tracker.Observe(cmd.Opcode);
            }

            if (maxVAInChunk >= 0)
                vaOffset += maxVAInChunk + 1;
        }
    }

    /// <summary>
    /// A VA select (0x40) sets the VA flag and clears the palette flag. A matrix palette
    /// (0x02) sets the palette flag. Step returns true, and clears both flags, for an
    /// IA-select seen while exactly one flag is set.
    /// </summary>
    private struct PaletteTracker
    {
        private bool _vaSeen;
        private bool _paletteSeen;

        public bool Step(byte opcode)
        {
            if (!RenderCommandStream.IsIndexSelect(opcode) || _vaSeen == _paletteSeen) return false;
            _vaSeen = false;
            _paletteSeen = false;
            return true;
        }

        public void Observe(byte opcode)
        {
            if (opcode == 0x40)
            {
                _vaSeen = true;
                _paletteSeen = false;
            }
            else if (opcode == 0x02)
            {
                _paletteSeen = true;
            }
        }
    }
}
