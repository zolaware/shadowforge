/**
 * @file        Formats/HDB/VertexFormat.cs
 * @brief       Vertex format bitfield decoder
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */
namespace ShadowForge.Formats.HDB;

public enum VertexSemantic
{
    Position,             // bit 12: 12 bytes, float3
    Influence1PosWeight,  // bit 13: 16 bytes, float4
    Normal,               // bit 14: 8 bytes, short4
    NormalTangent,        // bit 15: 8 bytes, short4
    Color,                // bit 16: 4 bytes, ubyte4
    TexCoord0,            // bit 17: 8 bytes
    TexCoord1,            // bit 18: 4 bytes
    TexCoord2,            // bit 19: 8 bytes
    TexCoord4,            // bit 20: 4 bytes (no TexCoord3 in this format)
    TangentBasis,         // bit 21: 4 bytes
    Influence2PosWeight,  // bit 22: 16 bytes
    Influence2NormIndex,  // bit 23: 8 bytes
    Influence3PosWeight,  // bit 24: 16 bytes
    Influence3NormIndex,  // bit 25: 8 bytes
    Influence4PosWeight,  // bit 26: 16 bytes
    Influence4NormIndex,  // bit 27: 8 bytes
    Binormal,             // bit 28: 8 bytes
}

public record VertexComponent(VertexSemantic Semantic, int Size, int BitIndex);

public static class VertexFormat
{
    private static readonly VertexComponent[] ComponentTable =
    [
        new(VertexSemantic.Position,            12, 12),
        new(VertexSemantic.Influence1PosWeight, 16, 13),
        new(VertexSemantic.Normal,               8, 14),
        new(VertexSemantic.NormalTangent,         8, 15),
        new(VertexSemantic.Color,                 4, 16),
        new(VertexSemantic.TexCoord0,             8, 17),
        new(VertexSemantic.TexCoord1,             4, 18),
        new(VertexSemantic.TexCoord2,             8, 19),
        new(VertexSemantic.TexCoord4,             4, 20),
        new(VertexSemantic.TangentBasis,          4, 21),
        new(VertexSemantic.Influence2PosWeight,  16, 22),
        new(VertexSemantic.Influence2NormIndex,   8, 23),
        new(VertexSemantic.Influence3PosWeight,  16, 24),
        new(VertexSemantic.Influence3NormIndex,   8, 25),
        new(VertexSemantic.Influence4PosWeight,  16, 26),
        new(VertexSemantic.Influence4NormIndex,   8, 27),
        new(VertexSemantic.Binormal,              8, 28),
    ];

    /// <summary>
    /// Base components (Position, Influence1PosWeight, Normal) that are implicitly
    /// present in multi-influence vertex formats even when their bits are not set.
    /// Their combined size (12 + 16 + 8 = 36 bytes) is added when the low 12 bits
    /// of vaType are zero (i.e. stride is computed purely from component flags).
    /// </summary>
    private static readonly int BaseComponentSize = 12 + 16 + 8; // 36 bytes

    private static readonly HashSet<VertexSemantic> BoneInfluenceSemantics = new()
    {
        VertexSemantic.Influence1PosWeight,
        VertexSemantic.Influence2PosWeight,
        VertexSemantic.Influence3PosWeight,
        VertexSemantic.Influence4PosWeight,
    };

    public static List<VertexComponent> GetComponents(uint vaType)
    {
        int baseBits = (int)(vaType & 0xFFF);
        var result = new List<VertexComponent>();

        foreach (var comp in ComponentTable)
        {
            bool isSet = (vaType & (1u << comp.BitIndex)) != 0;
            bool isImplicitBase = baseBits == 0 && comp.BitIndex <= 14;

            if (isSet || isImplicitBase)
                result.Add(comp);
        }
        return result;
    }

    public static int ComputeStride(uint vaType)
    {
        int baseBits = (int)(vaType & 0xFFF);

        // When the low 12 bits encode a non-zero value, that is the stride directly.
        if (baseBits != 0)
            return baseBits;

        // When low 12 bits are zero, compute stride from component flags.
        // Base components (Position + Influence1 + Normal = 36 bytes) are always
        // present in flagged formats, plus the sizes of all flagged optional components.
        int stride = BaseComponentSize;
        foreach (var comp in ComponentTable)
        {
            // Skip the base components (bits 12-14) since they are already counted
            if (comp.BitIndex <= 14)
                continue;

            if ((vaType & (1u << comp.BitIndex)) != 0)
                stride += comp.Size;
        }
        return stride;
    }

    public static int CountBoneInfluences(uint vaType)
    {
        bool hasAnyInfluence = false;
        int count = 0;

        foreach (var comp in ComponentTable)
        {
            if (BoneInfluenceSemantics.Contains(comp.Semantic)
                && (vaType & (1u << comp.BitIndex)) != 0)
            {
                count++;
                hasAnyInfluence = true;
            }
        }

        // If any higher influence bits are set, the base influence (Influence1) is
        // implicitly present even when bit 13 is not set.
        if (hasAnyInfluence && (vaType & (1u << 13)) == 0)
            count++;

        return count;
    }
}
