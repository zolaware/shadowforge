/**
 * @file        Formats/HDB/VertexDecoder.cs
 * @brief       Decodes raw vertex array bytes into Vertex objects
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */
using ShadowForge.IO;

namespace ShadowForge.Formats.HDB;

public static class VertexDecoder
{
    /// <summary>
    /// Decodes raw big-endian vertex data into a list of <see cref="Vertex"/> objects.
    /// Supports the two known Blue Dragon vertex layouts:
    ///   - 100-byte (0x13F00000): 3-influence skinned mesh
    ///   - 48-byte (other): single-influence or rigid mesh
    /// </summary>
    public static List<Vertex> Decode(byte[] rawData, uint vaType, int vertexCount)
    {
        if (vertexCount <= 0) return new List<Vertex>();

        // Compute stride from actual data size rather than the bitfield, which
        // doesn't always match the on-disc layout.
        int stride = rawData.Length / vertexCount;
        var vertices = new List<Vertex>(vertexCount);

        for (int i = 0; i < vertexCount; i++)
        {
            int off = i * stride;
            if (off + stride > rawData.Length) break;

            var v = stride >= 100
                ? DecodeSkinned100(rawData, off)
                : DecodeRigid48(rawData, off);
            vertices.Add(v);
        }

        return vertices;
    }

    /// <summary>
    /// 100-byte 3-influence skinned vertex.
    /// Python struct: >ffffhhhBxxxxxhhxxxxxxxxxxxxffffhhhBxffffhhhBxxxxxxxxx
    /// </summary>
    private static Vertex DecodeSkinned100(byte[] data, int off)
    {
        var v = new Vertex();

        // Influence 1 position + weight (16 bytes at offset 0)
        float px1 = BigEndian.ReadFloat(data, off);
        float py1 = BigEndian.ReadFloat(data, off + 4);
        float pz1 = BigEndian.ReadFloat(data, off + 8);
        float weight1 = BigEndian.ReadFloat(data, off + 12);
        v.PosX = px1; v.PosY = py1; v.PosZ = pz1;

        // Normal (short3 at offset 16)
        v.NormalX = BigEndian.ReadInt16(data, off + 16);
        v.NormalY = BigEndian.ReadInt16(data, off + 18);
        v.NormalZ = BigEndian.ReadInt16(data, off + 20);

        // Bone palette index 1 (byte at offset 22, divide by 2)
        int boneIdx1 = data[off + 22] / 2;

        // UV (short2 at offset 28, convert: value/512 - 32)
        v.U = BigEndian.ReadInt16(data, off + 28) / 512f - 32f;
        v.V = 1f - (BigEndian.ReadInt16(data, off + 30) / 512f - 32f);

        // Influence 2 position + weight (float4 at offset 44)
        float px2 = BigEndian.ReadFloat(data, off + 44);
        float py2 = BigEndian.ReadFloat(data, off + 48);
        float pz2 = BigEndian.ReadFloat(data, off + 52);
        float weight2 = BigEndian.ReadFloat(data, off + 56);
        int boneIdx2 = data[off + 66] / 2;

        // Influence 3 position + weight (float4 at offset 68)
        float px3 = BigEndian.ReadFloat(data, off + 68);
        float py3 = BigEndian.ReadFloat(data, off + 72);
        float pz3 = BigEndian.ReadFloat(data, off + 76);
        float weight3 = BigEndian.ReadFloat(data, off + 80);
        int boneIdx3 = data[off + 90] / 2;

        v.Influences.Add(new BoneInfluence { PaletteIndex = boneIdx1, Weight = weight1, PosX = px1, PosY = py1, PosZ = pz1 });
        if (weight2 != 0f)
            v.Influences.Add(new BoneInfluence { PaletteIndex = boneIdx2, Weight = weight2, PosX = px2, PosY = py2, PosZ = pz2 });
        if (weight3 != 0f)
            v.Influences.Add(new BoneInfluence { PaletteIndex = boneIdx3, Weight = weight3, PosX = px3, PosY = py3, PosZ = pz3 });

        return v;
    }

    /// <summary>
    /// 48-byte single-influence or rigid vertex.
    /// Python struct: >fffhhhBxxxxxhhxxxxxxxxxxxxxxxxxxxx (bone variant)
    ///                >fffhhhxxxxxxhhxxxxxxxxxxxxxxxxxxxx (rigid variant)
    /// Rigid when short at offset 18 == 0x7FFF.
    /// </summary>
    private static Vertex DecodeRigid48(byte[] data, int off)
    {
        var v = new Vertex();

        // Position (float3 at offset 0)
        v.PosX = BigEndian.ReadFloat(data, off);
        v.PosY = BigEndian.ReadFloat(data, off + 4);
        v.PosZ = BigEndian.ReadFloat(data, off + 8);

        // Normal (short3 at offset 12)
        v.NormalX = BigEndian.ReadInt16(data, off + 12);
        v.NormalY = BigEndian.ReadInt16(data, off + 14);
        v.NormalZ = BigEndian.ReadInt16(data, off + 16);

        // Bone index (byte at offset 18, or rigid sentinel)
        short sentinel = BigEndian.ReadInt16(data, off + 18);
        int boneIdx = sentinel == 0x7FFF ? 0 : data[off + 18] / 2;
        v.Influences.Add(new BoneInfluence { PaletteIndex = boneIdx, Weight = 1f, PosX = v.PosX, PosY = v.PosY, PosZ = v.PosZ });

        // UV (short2 at offset 24, convert: value/512 - 32)
        v.U = BigEndian.ReadInt16(data, off + 24) / 512f - 32f;
        v.V = 1f - (BigEndian.ReadInt16(data, off + 26) / 512f - 32f);

        return v;
    }
}
