using Microsoft.Extensions.Logging.Abstractions;
using ShadowForge.Formats.GLTF;
using ShadowForge.Formats.HDB;
using ShadowForge.Formats.HDB.Import;
using ShadowForge.Tests.GameData;

namespace ShadowForge.Tests.HDB;

public sealed class ModelCookerTests
{
    /// <summary>
    /// Importer output stores the root bone first where retail stores it last, so a model
    /// that went through export and import must still bake with the shipped parent links.
    /// </summary>
    [SkippableFact]
    public void Bake_ImporterOutput_KeepsEveryBonesParent()
    {
        byte[] shipped = RetailData.Read(@"chara\ply\pc04\pc04_obj.hdb");
        var stock = ModelCooker.Bake(ModelReader.Read(shipped));
        string dir = GameDataFixture.NewTempDir("sf_reimport_");
        string glb = Path.Combine(dir, "pc04.glb"), hdb = Path.Combine(dir, "pc04_obj.hdb");

        ModelExporter.Export(shipped, glb);
        ModelImporter.Import(glb, hdb, NullLogger.Instance);
        var reimported = ModelCooker.Bake(ModelReader.Read(File.ReadAllBytes(hdb)));

        static Dictionary<string, string> ParentNames(ModelFile m) => m.Bones
            .Where(b => b.Name != "Dummy")
            .ToDictionary(b => b.Name, b => b.ParentIndex < 0 ? "" : m.Bones[b.ParentIndex].Name);
        Assert.Equal(ParentNames(stock), ParentNames(reimported));
    }

    [Fact]
    public void Bake_HeaderMagicIsHDB()
    {
        var model = SampleModel.Cooked();
        Assert.Equal(0x42444840u, model.Header.Magic);
    }

    [Fact]
    public void Bake_BonesAndTexturesNonEmpty()
    {
        var model = SampleModel.Cooked();
        Assert.NotEmpty(model.Bones);
        Assert.NotEmpty(model.Textures);
        Assert.Equal(model.Textures.Count, model.TextureCount);
    }

    [Fact]
    public void Bake_BonesHaveNames()
    {
        var model = SampleModel.Cooked();
        Assert.All(model.Bones, b => Assert.False(string.IsNullOrEmpty(b.Name)));
    }

    [Fact]
    public void Bake_VertexArraysPopulated()
    {
        var model = SampleModel.Cooked();
        Assert.NotEmpty(model.VertexArrays);
        Assert.All(model.VertexArrays, va => Assert.True(va.RawVertices.Length > 0));
    }

    [Fact]
    public void Bake_MeshGroupsAndIndexArraysPopulated()
    {
        var model = SampleModel.Cooked();
        Assert.NotEmpty(model.MeshGroups);
        Assert.NotEmpty(model.IndexArrays);

        Assert.All(model.MeshGroups, g =>
        {
            Assert.InRange(g.VAIndex, 0, model.VertexArrays.Count - 1);
            Assert.InRange(g.IAIndex, 0, model.IndexArrays.Count - 1);
        });
    }

    [Fact]
    public void Bake_SecondTablePreserved()
    {
        var raw = ModelReader.Read(File.ReadAllBytes(TestFile.HDB));
        var model = ModelCooker.Bake(raw);
        Assert.Equal(raw.SecondTable.Entries, model.SecondTable.Entries);
    }

    /// <summary>
    /// Synthetic RC stream exercising the stage-1/2 texture binding rule:
    /// stage words (0x61/0x62) are sticky across draws until the next
    /// stage-0 (0x60) bind, which resets both stage fields to -1.
    ///
    /// Stream (opcode, data bytes):
    ///   0x40 00 00 00        VA select, so the first draw gets a palette
    ///   0x60 00              mat 0
    ///   0x10 [5x00]          draw A -> mat 0, stage1 -1, stage2 -1
    ///   0x01 03              RenderCommandStream.StagedStateOpen, leaves material and
    ///                        stage state alone
    ///   0x61 04              stage1 = 4
    ///   0x62 05              stage2 = 5
    ///   0x05 FF              RenderCommandStream.StagedStateClose, likewise
    ///   0x20 [5x00]          draw B -> mat 0, stage1 4, stage2 5
    ///   0x61 06              stage1 = 6
    ///   0x30 [5x00]          draw C -> mat 0, stage1 6, stage2 5 (sticky)
    ///   0x60 02              mat 2, resets both stage fields
    ///   0x10 [5x00]          draw D -> mat 2, stage1 -1, stage2 -1
    /// </summary>
    [Fact]
    public void BuildMeshGroups_StageBindingsAreStickyUntilStageZeroRebind()
    {
        byte[] rc =
        {
            0x40, 0x00, 0x00, 0x00,
            0x60, 0x00,
            0x10, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x03,
            0x61, 0x04,
            0x62, 0x05,
            0x05, 0xFF,
            0x20, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x61, 0x06,
            0x30, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x60, 0x02,
            0x10, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        var commands = RenderCommandStream.Parse(rc);

        var model = new ModelFile();
        model.RenderCommandChunks.Add(commands);
        for (int i = 0; i < 4; i++)
            model.IndexArrays.Add(new IndexArray());

        var matrixPalettes = new List<List<ushort>> { new List<ushort>() };

        ModelCooker.BuildMeshGroups(model, matrixPalettes);

        Assert.Equal(4, model.MeshGroups.Count);

        var a = model.MeshGroups[0];
        Assert.Equal(0, a.MaterialIndex);
        Assert.Equal(-1, a.Stage1TexIndex);
        Assert.Equal(-1, a.Stage2TexIndex);

        var b = model.MeshGroups[1];
        Assert.Equal(0, b.MaterialIndex);
        Assert.Equal(4, b.Stage1TexIndex);
        Assert.Equal(5, b.Stage2TexIndex);

        var c = model.MeshGroups[2];
        Assert.Equal(0, c.MaterialIndex);
        Assert.Equal(6, c.Stage1TexIndex);
        Assert.Equal(5, c.Stage2TexIndex);

        var d = model.MeshGroups[3];
        Assert.Equal(2, d.MaterialIndex);
        Assert.Equal(-1, d.Stage1TexIndex);
        Assert.Equal(-1, d.Stage2TexIndex);

        Assert.Equal(-1, model.IndexArrays[0].Stage1TexIndex);
        Assert.Equal(4, model.IndexArrays[1].Stage1TexIndex);
        Assert.Equal(5, model.IndexArrays[1].Stage2TexIndex);
        Assert.Equal(6, model.IndexArrays[2].Stage1TexIndex);
        Assert.Equal(-1, model.IndexArrays[3].Stage1TexIndex);
        Assert.Equal(-1, model.IndexArrays[3].Stage2TexIndex);
    }
}
