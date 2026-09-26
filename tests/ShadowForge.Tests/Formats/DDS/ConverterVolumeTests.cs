using ShadowForge.Formats.DDS;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ShadowForge.Tests.Formats.DDS;

/// <summary>
/// The pc05 fur volume (voltex_pc05_fur_01.36t) is a 256x256x8 DXT3 3D
/// texture. Every depth slice must come back as its own decodable PNG.
/// </summary>
public sealed class ConverterVolumeTests
{
    private const string Fixture = "testdata/textures/voltex_pc05_fur_01.36t";

    [Fact]
    public void VolumeSlices_OnePngPerDepthSlice()
    {
        var slices = Converter.ConvertVolumeToPngSlices(File.ReadAllBytes(Fixture));

        Assert.Equal(8, slices.Count);
        foreach (var png in slices)
        {
            using var img = Image.Load<Rgba32>(png);
            Assert.Equal(256, img.Width);
            Assert.Equal(256, img.Height);
        }
    }

    [Fact]
    public void VolumeSlices_FirstSliceMatchesSingleImageDecode()
    {
        byte[] raw = File.ReadAllBytes(Fixture);
        var slices = Converter.ConvertVolumeToPngSlices(raw);
        byte[] single = Converter.ConvertToPngBytes(raw, isVolume: true);

        Assert.Equal(single, slices[0]);
    }

    [Fact]
    public void VolumeSlices_DifferBetweenDepths()
    {
        var slices = Converter.ConvertVolumeToPngSlices(File.ReadAllBytes(Fixture));
        Assert.NotEqual(slices[0], slices[7]);
    }
}
