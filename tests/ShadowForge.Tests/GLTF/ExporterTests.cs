/**
 * @file        GLTF/ExporterTests.cs
 * @brief       Tests for glTF export from HDB models
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */
using ShadowForge.Formats.HDB;
using ShadowForge.Formats.GLTF;

namespace ShadowForge.Tests.Gltf;

public class ExporterTests
{
    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void Export_ProducesValidGlb(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var model = Reader.Read(path);
        var outputPath = Path.GetTempFileName() + ".glb";

        try
        {
            Exporter.Export(model, outputPath);
            Assert.True(File.Exists(outputPath));

            var bytes = File.ReadAllBytes(outputPath);
            Assert.True(bytes.Length > 100);
            // glTF binary magic: "glTF" (0x46546C67)
            Assert.Equal((byte)'g', bytes[0]);
            Assert.Equal((byte)'l', bytes[1]);
            Assert.Equal((byte)'T', bytes[2]);
            Assert.Equal((byte)'F', bytes[3]);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Theory]
    [InlineData("bs01_obj.hdb")]
    public void Export_GlbIsLoadable(string filename)
    {
        var path = Path.Combine("testdata", filename);
        if (!File.Exists(path)) return;

        var model = Reader.Read(path);
        var outputPath = Path.GetTempFileName() + ".glb";

        try
        {
            Exporter.Export(model, outputPath);
            var loaded = SharpGLTF.Schema2.ModelRoot.Load(outputPath);
            Assert.NotNull(loaded);
            Assert.NotEmpty(loaded.LogicalMeshes);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
