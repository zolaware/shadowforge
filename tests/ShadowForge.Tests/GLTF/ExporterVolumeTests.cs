using System.Numerics;
using System.Text.Json.Nodes;
using ShadowForge.Formats.DDS;
using ShadowForge.Formats.GLTF;
using ShadowForge.Formats.HDB;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ShadowForge.Tests.GLTF;

/// <summary>
/// A texture whose only file is a .36t volume (the pc05 fur shell, voltex_pc05_fur_01)
/// exports with slice 0 as baseColor and sfVolumeTexture and sfVolumeDepth extras, which
/// tell the importer not to cook it as a 2D DDS.
/// </summary>
public sealed class ExporterVolumeTests
{
    private const string Fixture = "testdata/textures/voltex_pc05_fur_01.36t";
    private const string FurName = "voltex_pc05_fur_01";

    /// <summary>
    /// One identity bone, two textures ("body" as a plain DDS, the fur volume
    /// as a .36t only), one skinned triangle per material.
    /// </summary>
    internal static ModelFile BuildModel()
    {
        var raw = new byte[3 * 100];
        ExportFixtures.EncodeSkinnedVertex(raw, 0, new Vector3(0, 0, 0), new Vector2(0.25f, 0.5f));
        ExportFixtures.EncodeSkinnedVertex(raw, 100, new Vector3(1, 0, 0), new Vector2(0.5f, 0.5f));
        ExportFixtures.EncodeSkinnedVertex(raw, 200, new Vector3(0, 1, 0), new Vector2(0.25f, 0.25f));

        return new ModelFile
        {
            Bones = { new Bone { Index = 0, Name = "root" } },
            Textures =
            {
                new TextureEntry { Name = "body" },
                new TextureEntry { Name = FurName },
            },
            TextureCount = 2,
            VertexArrays =
            {
                new VertexArray { VAType = 0, VertexCount = 3, RawVertices = raw },
            },
            IndexArrays =
            {
                new IndexArray { Indices = new ushort[] { 0, 1, 2 } },
                new IndexArray { Indices = new ushort[] { 0, 1, 2 } },
            },
            MeshGroups =
            {
                new MeshGroup { VAIndex = 0, IAIndex = 0, MaterialIndex = 0,
                                Topology = 0, BonePalette = { 0 } },
                new MeshGroup { VAIndex = 0, IAIndex = 1, MaterialIndex = 1,
                                Topology = 0, BonePalette = { 0 } },
            },
        };
    }

    /// <summary>
    /// Stages "body.dds" plus the real fur .36t in a fresh temp dir and
    /// exports the model against it. Caller deletes the dir.
    /// </summary>
    internal static (string Dir, SharpGLTF.Schema2.ModelRoot Gltf) ExportWithFur()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sf_vol_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ExportFixtures.WriteSolidDDS(dir, "body", 200, 50, 50);
        File.Copy(Fixture, Path.Combine(dir, FurName + ".36t"));
        string glb = Path.Combine(dir, "model.glb");
        SceneExporter.Export(BuildModel(), glb, textureDir: dir);
        return (dir, SharpGLTF.Schema2.ModelRoot.Load(glb));
    }

    [Fact]
    public void VolumeOnlyTexture_BindsSliceZeroAsBaseColor()
    {
        var (dir, gltf) = ExportWithFur();
        try
        {
            var fur = gltf.LogicalMaterials.Single(m => m.Name == FurName);
            var img = fur.FindChannel("BaseColor")?.Texture?.PrimaryImage;
            Assert.NotNull(img);
            Assert.Equal(FurName, img!.Name);

            using var decoded = Image.Load<Rgba32>(img.Content.Content.ToArray());
            Assert.Equal(256, decoded.Width);
            Assert.Equal(256, decoded.Height);

            byte[] expected = Converter.ConvertVolumeToPngSlices(File.ReadAllBytes(Fixture))[0];
            Assert.Equal(expected, img.Content.Content.ToArray());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void VolumeOnlyTexture_CarriesVolumeExtras()
    {
        var (dir, gltf) = ExportWithFur();
        try
        {
            var fur = gltf.LogicalMaterials.Single(m => m.Name == FurName);
            var extras = Assert.IsType<JsonObject>(fur.Extras);
            Assert.Equal(FurName + ".36t", (string?)extras["sfVolumeTexture"]);
            Assert.Equal(8, (int?)extras["sfVolumeDepth"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PlainDDSTexture_HasNoVolumeExtras()
    {
        var (dir, gltf) = ExportWithFur();
        try
        {
            var body = gltf.LogicalMaterials.Single(m => m.Name == "body");
            Assert.NotNull(body.FindChannel("BaseColor")?.Texture?.PrimaryImage);
            var extras = body.Extras as JsonObject;
            Assert.True(extras is null || !extras.ContainsKey("sfVolumeTexture"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DDSPreferredOverVolumeWhenBothExist()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sf_vol_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ExportFixtures.WriteSolidDDS(dir, "body", 200, 50, 50);
            ExportFixtures.WriteSolidDDS(dir, FurName, 50, 200, 50);
            File.Copy(Fixture, Path.Combine(dir, FurName + ".36t"));
            string glb = Path.Combine(dir, "model.glb");
            SceneExporter.Export(BuildModel(), glb, textureDir: dir);
            var gltf = SharpGLTF.Schema2.ModelRoot.Load(glb);

            var fur = gltf.LogicalMaterials.Single(m => m.Name == FurName);
            var img = fur.FindChannel("BaseColor")?.Texture?.PrimaryImage;
            Assert.NotNull(img);
            using var decoded = Image.Load<Rgba32>(img!.Content.Content.ToArray());
            Assert.Equal(4, decoded.Width);
            var extras = fur.Extras as JsonObject;
            Assert.True(extras is null || !extras.ContainsKey("sfVolumeTexture"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FindVolume_LocatesNestedFileCaseInsensitively()
    {
        string root = Path.Combine(Path.GetTempPath(), "sf_vol_" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(root, "chara", "ipk", "pc05");
        Directory.CreateDirectory(nested);
        try
        {
            File.Copy(Fixture, Path.Combine(nested, "VOLTEX_PC05_FUR_01.36T"));
            string? found = TextureResolver.FindVolume(FurName, root);
            Assert.NotNull(found);
            Assert.Equal("VOLTEX_PC05_FUR_01.36T", Path.GetFileName(found));
            Assert.Null(TextureResolver.FindDDS(FurName, root));
        }
        finally { Directory.Delete(root, true); }
    }
}
