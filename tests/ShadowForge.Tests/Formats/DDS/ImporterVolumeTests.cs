using ShadowForge.Formats.DDS;

namespace ShadowForge.Tests.Formats.DDS;

/// <summary>
/// The pc05 fur volume stores its size in fetch dword 2 as 256x256x8. Byte 0x36 holds the
/// fetch constant's dimension field, which reads 4 for every volume, so it is not the depth.
/// </summary>
public sealed class ImporterVolumeTests
{
    private const string Fixture = "testdata/textures/voltex_pc05_fur_01.36t";

    [Fact]
    public void ReadVolumeSize_TakesDepthFromFetchDword2()
    {
        var (edge, depth) = Header.ReadVolumeSize(File.ReadAllBytes(Fixture));

        Assert.Equal(256, edge);
        Assert.Equal(8, depth);
    }

    [Fact]
    public void ConvertAndSave_WritesEverySliceImportVolumeExpects()
    {
        string dir = Directory.CreateTempSubdirectory("sforge-voltex-").FullName;
        try
        {
            int saved = Converter.ConvertAndSave(Fixture, Path.Combine(dir, "fur.png"));

            Assert.Equal(8, saved);
            Assert.Equal(8, Directory.GetFiles(dir, "fur_slice_???.png").Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ImportVolume_AcceptsEverySliceTheReferenceHolds()
    {
        string dir = Directory.CreateTempSubdirectory("sforge-voltex-").FullName;
        try
        {
            var slices = Converter.ConvertVolumeToPngSlices(File.ReadAllBytes(Fixture));
            string[] paths = slices.Select((png, z) =>
            {
                string path = Path.Combine(dir, $"slice_{z}.png");
                File.WriteAllBytes(path, png);
                return path;
            }).ToArray();
            string output = Path.Combine(dir, "out.36t");

            Importer.ImportVolumeWithReference(paths, Fixture, output);

            byte[] imported = File.ReadAllBytes(output);
            Assert.Equal(new FileInfo(Fixture).Length, imported.Length);
            Assert.Equal(8, Converter.ConvertVolumeToPngSlices(imported).Count);
        }
        finally { Directory.Delete(dir, true); }
    }
}
