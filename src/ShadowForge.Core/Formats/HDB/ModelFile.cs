/**
 * @file        Formats/HDB/ModelFile.cs
 * @brief       HDB model file data types: header, bones, textures, geometry, render commands
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */

using ShadowForge.IO;

namespace ShadowForge.Formats.HDB;

/// <summary>Top-level container for a parsed HDB model file.</summary>
public class ModelFile
{
    public FileHeader Header { get; set; } = new();
    public List<Bone> Bones { get; set; } = new();
    public List<TextureEntry> Textures { get; set; } = new();
    public int TextureCount { get; set; }
    public List<VertexArray> VertexArrays { get; set; } = new();
    public List<IndexArray> IndexArrays { get; set; } = new();
    public List<RenderCommand> RenderCommands { get; set; } = new();
    public List<MeshGroup> MeshGroups { get; set; } = new();
    public List<List<ushort>> MatrixPalettes { get; set; } = new();
    public SecondTableData SecondTable { get; set; } = new();
    public List<RawFirstTableEntry> RawEntries { get; set; } = new();
    public int OriginalFileSize { get; set; }
}

/// <summary>32-byte file header beginning with the BDH@ magic.</summary>
public class FileHeader
{
    public const uint Magic = 0x40484442; // "BDH@"
    public const int MinSize = 24;

    public byte[] RawData { get; set; } = new byte[32];

    public uint MagicValue      => BigEndian.ReadUInt32(RawData, 0x00);
    public uint Flags           => BigEndian.ReadUInt32(RawData, 0x04);
    public uint UnkField1       => BigEndian.ReadUInt32(RawData, 0x08);
    public uint UnkField2       => BigEndian.ReadUInt32(RawData, 0x0C);
    public uint UnkField3       => BigEndian.ReadUInt32(RawData, 0x10);
    public uint FirstTableOffset => BigEndian.ReadUInt32(RawData, 0x14);
}

/// <summary>Skeleton bone with transform data and hierarchy links.</summary>
public class Bone
{
    public int Index { get; set; }
    public int HFlag { get; set; }

    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }

    public float EulerX { get; set; }
    public float EulerY { get; set; }
    public float EulerZ { get; set; }

    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public float ScaleZ { get; set; } = 1f;

    public float[] ExtraEuler { get; set; } = new float[6];

    public int ChildIndex { get; set; } = -1;
    public int ParentIndex { get; set; } = -1;

    public string Name { get; set; } = "";

    /// <summary>Raw bytes preserved for round-trip fidelity.</summary>
    public byte[] RawData { get; set; } = Array.Empty<byte>();
}

/// <summary>28-byte texture name record from the texture table.</summary>
public class TextureEntry
{
    public string Name { get; set; } = "";
    public byte[] RawData { get; set; } = new byte[28];
}

/// <summary>Vertex array region describing a block of raw vertex data.</summary>
public class VertexArray
{
    /// <summary>Vertex format bitfield identifying present attribute channels.</summary>
    public uint VaType { get; set; }
    public int VaOffset { get; set; }
    public int VaSize { get; set; }
    public byte[] RawVertices { get; set; } = Array.Empty<byte>();
}

/// <summary>Decoded vertex with position, normal, UV, and skinning influences.</summary>
public class Vertex
{
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }

    public short NormalX { get; set; }
    public short NormalY { get; set; }
    public short NormalZ { get; set; }

    public float U { get; set; }
    public float V { get; set; }

    public List<BoneInfluence> Influences { get; set; } = new();
}

/// <summary>Single bone weight contribution for a skinned vertex.</summary>
public class BoneInfluence
{
    public int PaletteIndex { get; set; }
    public float Weight { get; set; }
}

/// <summary>Index array region referencing a range of primitive indices.</summary>
public class IndexArray
{
    /// <summary>Byte size of the index region.</summary>
    public int Size { get; set; }

    /// <summary>Byte offset to the start of index data.</summary>
    public int Start { get; set; }

    /// <summary>Primitive type: 0x10 = list, 0x20/0x30 = strip.</summary>
    public int Type { get; set; }

    public int MaterialIndex { get; set; }

    public ushort[] Indices { get; set; } = Array.Empty<ushort>();
}

/// <summary>Render command opcodes found in the HDB command stream.</summary>
public enum RenderCommandType : byte
{
    End             = 0x00,
    MatrixPalette   = 0x02,
    IaSelectList    = 0x10,
    Section1        = 0x19,
    IaSelectStrip1  = 0x20,
    IaSelectStrip2  = 0x30,
    VaSelect        = 0x40,
    Indicate        = 0x50,
    MaterialSelect  = 0x60,
    DiffuseSpecular = 0x93,
    Section2        = 0x94,
}

/// <summary>Single entry in the HDB render command stream.</summary>
public class RenderCommand
{
    public byte Opcode { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

/// <summary>Logical mesh group binding a vertex array, index array, material, and bone palette.</summary>
public class MeshGroup
{
    public int VaIndex { get; set; }
    public int IaIndex { get; set; }
    public int MaterialIndex { get; set; }
    public int Topology { get; set; }
    public List<ushort> BonePalette { get; set; } = new();
}

/// <summary>Parsed second-table block containing a variable-length integer array.</summary>
public class SecondTableData
{
    public int EntryCount { get; set; }
    public int TableLength { get; set; }
    public int[] Entries { get; set; } = Array.Empty<int>();
    public int Padding { get; set; }
}

/// <summary>Raw first-table entry preserving type, offset, length, and payload bytes.</summary>
public class RawFirstTableEntry
{
    public int EntryType { get; set; }
    public int DataOffset { get; set; }
    public int DataLength { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
