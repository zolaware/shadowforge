using ShadowForge.Formats.HDB;
using Xunit.Abstractions;

namespace ShadowForge.Tests.Hdb;

public class TransformDiagnostic
{
    private readonly ITestOutputHelper _out;
    public TransformDiagnostic(ITestOutputHelper output) => _out = output;

    [Fact]
    public void DumpBoneHierarchy()
    {
        var path = Path.Combine("testdata", "bs01_obj.hdb");
        if (!File.Exists(path)) return;
        var model = Reader.Read(path);

        // Check if bone indices are contiguous
        _out.WriteLine($"Bones: {model.Bones.Count}");
        for (int i = 0; i < Math.Min(20, model.Bones.Count); i++)
        {
            var b = model.Bones[i];
            _out.WriteLine($"  [{i}] idx={b.Index} name=\"{b.Name}\" parent={b.ParentIndex} child={b.ChildIndex} pos=({b.PosX:F3},{b.PosY:F3},{b.PosZ:F3}) euler=({b.EulerX:F4},{b.EulerY:F4},{b.EulerZ:F4}) scale=({b.ScaleX:F3},{b.ScaleY:F3},{b.ScaleZ:F3})");
        }

        // Check for index gaps
        var indices = model.Bones.Select(b => b.Index).ToList();
        bool contiguous = indices.SequenceEqual(Enumerable.Range(0, model.Bones.Count));
        _out.WriteLine($"\nIndices contiguous 0..{model.Bones.Count - 1}: {contiguous}");
        if (!contiguous)
        {
            var missing = Enumerable.Range(0, indices.Max() + 1).Except(indices).Take(10);
            _out.WriteLine($"Missing indices: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void DumpPaletteMappings()
    {
        var path = Path.Combine("testdata", "bs01_obj.hdb");
        if (!File.Exists(path)) return;
        var model = Reader.Read(path);

        _out.WriteLine($"MeshGroups: {model.MeshGroups.Count}");
        for (int i = 0; i < Math.Min(5, model.MeshGroups.Count); i++)
        {
            var g = model.MeshGroups[i];
            _out.WriteLine($"\nGroup[{i}]: VA={g.VaIndex} IA={g.IaIndex} mat={g.MaterialIndex} topo=0x{g.Topology:X2}");
            _out.WriteLine($"  Palette({g.BonePalette.Count}): [{string.Join(", ", g.BonePalette)}]");

            // Decode first few vertices and show their bone lookups
            if (g.VaIndex < model.VertexArrays.Count)
            {
                var va = model.VertexArrays[g.VaIndex];
                var verts = VertexDecoder.Decode(va.RawVertices, va.VaType, Math.Min(va.VertexCount, 3));
                foreach (var (v, vi) in verts.Select((v, i) => (v, i)))
                {
                    var inf = v.Influences.FirstOrDefault();
                    if (inf == null) continue;
                    int palIdx = inf.PaletteIndex;
                    int boneIdx = palIdx < g.BonePalette.Count ? g.BonePalette[palIdx] : -1;
                    string boneName = boneIdx >= 0 && boneIdx < model.Bones.Count ? model.Bones[boneIdx].Name : "???";
                    _out.WriteLine($"  V[{vi}]: pos=({v.PosX:F2},{v.PosY:F2},{v.PosZ:F2}) palIdx={palIdx} -> bone={boneIdx} \"{boneName}\" w={inf.Weight:F3}");
                }
            }
        }
    }

    [Fact]
    public void DumpVertexRawBytes()
    {
        var path = Path.Combine("testdata", "bs01_obj.hdb");
        if (!File.Exists(path)) return;
        var model = Reader.Read(path);

        // Dump first few raw bytes of first VA to verify decoding
        var va = model.VertexArrays[0];
        int stride = va.RawVertices.Length / va.VertexCount;
        _out.WriteLine($"VA[0]: type=0x{va.VaType:X8} count={va.VertexCount} stride={stride}");
        _out.WriteLine($"First vertex raw hex ({stride} bytes):");
        _out.WriteLine(Convert.ToHexString(va.RawVertices[..stride]));

        // Manual decode of first vertex for verification
        var data = va.RawVertices;
        float x = ShadowForge.IO.BigEndian.ReadFloat(data, 0);
        float y = ShadowForge.IO.BigEndian.ReadFloat(data, 4);
        float z = ShadowForge.IO.BigEndian.ReadFloat(data, 8);
        float w = ShadowForge.IO.BigEndian.ReadFloat(data, 12);
        short nx = ShadowForge.IO.BigEndian.ReadInt16(data, 16);
        short ny = ShadowForge.IO.BigEndian.ReadInt16(data, 18);
        short nz = ShadowForge.IO.BigEndian.ReadInt16(data, 20);
        byte boneIdxRaw = data[22];
        short uvx = ShadowForge.IO.BigEndian.ReadInt16(data, 28);
        short uvy = ShadowForge.IO.BigEndian.ReadInt16(data, 30);
        _out.WriteLine($"pos=({x:F4},{y:F4},{z:F4}) weight={w:F4}");
        _out.WriteLine($"normal=({nx},{ny},{nz}) boneIdxRaw={boneIdxRaw} boneIdx={boneIdxRaw / 2}");
        _out.WriteLine($"uvRaw=({uvx},{uvy}) uv=({uvx / 512f - 32f:F4},{1f - (uvy / 512f - 32f):F4})");
    }
}
