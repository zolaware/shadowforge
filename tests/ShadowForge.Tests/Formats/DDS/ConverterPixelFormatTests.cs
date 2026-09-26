using ShadowForge.Formats.DDS;
using ShadowForge.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ShadowForge.Tests.Formats.DDS;

/// <summary>
/// Uncompressed formats decode to RGBA PNGs, in 2D and as volume slices. Volumes reuse the
/// pc05 fur header with its format and depth rewritten, since no retail volume uses these formats.
/// </summary>
public sealed class ConverterPixelFormatTests : IDisposable
{
    private const string VolumeFixture = "testdata/textures/voltex_pc05_fur_01.36t";
    private const int FormatByte = 0x27;
    private const int FetchDword2 = 0x28;

    private readonly string _dir = Directory.CreateTempSubdirectory("sf-dds-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(GraphicFormat.TextureFormatL8, 32)]
    [InlineData(GraphicFormat.TextureFormatA8L8, 32)]
    [InlineData(GraphicFormat.TextureFormatL8, 64)]
    [InlineData(GraphicFormat.TextureFormatA8L8, 64)]
    public void Luminance2D_DecodesToGreyRgba(GraphicFormat format, int size)
    {
        byte[] rgba = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++)
        {
            byte v = (byte)(i * 7);
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v;
            rgba[i * 4 + 3] = (byte)(255 - i);
        }

        byte[] dds = Importer.ImportFromRgba(rgba, size, size, format);
        using var img = Image.Load<Rgba32>(Converter.ConvertToPngBytes(dds, isVolume: false));

        Assert.Equal(size, img.Width);
        Assert.Equal(size, img.Height);
        for (int i = 0; i < size * size; i++)
        {
            Rgba32 p = img[i % size, i / size];
            byte v = (byte)(i * 7);
            Assert.InRange(p.R, v - 1, v);
            Assert.Equal(p.R, p.G);
            Assert.Equal(p.R, p.B);
            Assert.Equal(format == GraphicFormat.TextureFormatA8L8 ? (byte)(255 - i) : (byte)255, p.A);
        }
    }

    /// <summary>
    /// A 32-wide texture at 1 or 2 bytes per pixel tiles over more than 32 x 32 x bpp bytes;
    /// the data is padded to whole 4 KB pages, which every tiled layout fits in.
    /// </summary>
    [Theory]
    [InlineData(GraphicFormat.TextureFormatL8, 32, 32, 4096)]
    [InlineData(GraphicFormat.TextureFormatA8L8, 32, 32, 4096)]
    [InlineData(GraphicFormat.TextureFormatR5G6B5, 16, 32, 4096)]
    [InlineData(GraphicFormat.TextureFormatL8, 64, 32, 4096)]
    [InlineData(GraphicFormat.TextureFormatA8R8G8B8, 32, 32, 4096)]
    [InlineData(GraphicFormat.TextureFormatDXT1, 128, 128, 8192)]
    [InlineData(GraphicFormat.TextureFormatDXT5, 32, 32, 16384)]
    [InlineData(GraphicFormat.TextureFormatA8R8G8B8, 64, 64, 16384)]
    public void SwizzledDataSize_IsWholePages(GraphicFormat format, int width, int height, int expected)
        => Assert.Equal(expected, Header.ComputeSwizzledDataSize(width, height, format));

    [Theory]
    [InlineData(GraphicFormat.TextureFormatR5G6B5)]
    [InlineData(GraphicFormat.TextureFormatX4R4G4B4)]
    public void Packed16_Small2D_KeepsEveryPixel(GraphicFormat format)
    {
        const int w = 16, h = 32;
        byte[] rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = 255;
        }

        byte[] dds = Importer.ImportFromRgba(rgba, w, h, format);
        using var img = Image.Load<Rgba32>(Converter.ConvertToPngBytes(dds, isVolume: false));

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            Assert.Equal(new Rgba32(255, 255, 255, 255), img[x, y]);
    }

    [Fact]
    public void L8_IsStoredWithoutByteSwap()
    {
        const int size = 64;
        byte[] rgba = new byte[size * size * 4];
        rgba[0] = rgba[1] = rgba[2] = 200;
        byte[] dds = Importer.ImportFromRgba(rgba, size, size, GraphicFormat.TextureFormatL8);

        Assert.Equal(0, dds[FormatByte] >> 6);
        Assert.Equal(200, dds[Header.HeaderSize]);
        Assert.Equal(0, dds[Header.HeaderSize + 1]);
    }

    [Theory]
    [InlineData(GraphicFormat.TextureFormatR5G6B5, 0x44)]
    [InlineData(GraphicFormat.TextureFormatX4R4G4B4, 0x4F)]
    [InlineData(GraphicFormat.TextureFormatA8L8, 0x4A)]
    [InlineData(GraphicFormat.TextureFormatL8, 0x02)]
    public void Volume_RoundTripsEverySlice(GraphicFormat format, byte formatByte)
    {
        byte[] reference = File.ReadAllBytes(VolumeFixture);
        const int edge = 256, depth = 4;
        int dataSize = edge * edge * depth * format.BytesPerUnit();
        Array.Resize(ref reference, Header.HeaderSize + dataSize);
        reference[FormatByte] = formatByte;
        BigEndian.WriteUInt32(reference, FetchDword2, (uint)(edge - 1) | ((uint)(edge - 1) << 11) | ((uint)(depth - 1) << 22));
        string refPath = Path.Combine(_dir, "ref.36t");
        File.WriteAllBytes(refPath, reference);

        Rgba32[] colors = format is GraphicFormat.TextureFormatA8L8 or GraphicFormat.TextureFormatL8
            ? [new(0, 0, 0, 255), new(255, 255, 255, 255), new(0, 0, 0, 255), new(255, 255, 255, 255)]
            : [new(255, 0, 0, 255), new(0, 255, 0, 255), new(0, 0, 255, 255), new(255, 255, 255, 255)];
        string[] slicePaths = new string[depth];
        for (int z = 0; z < depth; z++)
        {
            slicePaths[z] = Path.Combine(_dir, $"slice{z}.png");
            using var slice = new Image<Rgba32>(edge, edge, colors[z]);
            slice.SaveAsPng(slicePaths[z]);
        }

        string outPath = Path.Combine(_dir, "out.36t");
        Importer.ImportVolumeWithReference(slicePaths, refPath, outPath);
        var pngs = Converter.ConvertVolumeToPngSlices(File.ReadAllBytes(outPath));

        Assert.Equal(depth, pngs.Count);
        for (int z = 0; z < depth; z++)
        {
            using var img = Image.Load<Rgba32>(pngs[z]);
            Assert.Equal(colors[z], img[0, 0]);
            Assert.Equal(colors[z], img[edge - 1, edge - 1]);
        }
    }
}
