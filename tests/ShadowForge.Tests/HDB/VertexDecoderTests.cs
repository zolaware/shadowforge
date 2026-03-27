/**
 * @file        HDB/VertexDecoderTests.cs
 * @brief       Tests for HDB vertex decoding against real model data
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */
using ShadowForge.Formats.HDB;

namespace ShadowForge.Tests.Hdb;

public class VertexDecoderTests
{
    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void Decode_AllVAs_ReturnsExpectedVertexCount(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var model = Reader.Read(path);

        foreach (var va in model.VertexArrays)
        {
            var vertices = VertexDecoder.Decode(va.RawVertices, va.VaType, va.VertexCount);
            Assert.Equal(va.VertexCount, vertices.Count);
        }
    }

    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void Decode_Positions_AreFinite(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var model = Reader.Read(path);
        var va = model.VertexArrays[0];
        var vertices = VertexDecoder.Decode(va.RawVertices, va.VaType, va.VertexCount);

        Assert.All(vertices, v =>
        {
            Assert.False(float.IsNaN(v.PosX) || float.IsInfinity(v.PosX));
            Assert.False(float.IsNaN(v.PosY) || float.IsInfinity(v.PosY));
            Assert.False(float.IsNaN(v.PosZ) || float.IsInfinity(v.PosZ));
        });
    }

    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void Decode_UVs_AreInReasonableRange(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var model = Reader.Read(path);
        var va = model.VertexArrays[0];
        var vertices = VertexDecoder.Decode(va.RawVertices, va.VaType, va.VertexCount);

        Assert.All(vertices, v =>
        {
            Assert.InRange(v.U, -64f, 64f);
            Assert.InRange(v.V, -64f, 64f);
        });
    }

    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void Decode_Skinned_HasInfluences(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var model = Reader.Read(path);
        var skinned = model.VertexArrays.FirstOrDefault(va => va.VaType == 0x13F00000);
        if (skinned == null) return;

        var vertices = VertexDecoder.Decode(skinned.RawVertices, skinned.VaType, skinned.VertexCount);
        Assert.All(vertices, v => Assert.NotEmpty(v.Influences));
    }
}
