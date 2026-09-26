using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ShadowForge.Formats.DDS;

/// <summary>
/// Decodes Xbox 360 textures to PNG. A .dds path is a 2D texture, any other extension
/// (.36t) a volume. <c>performGlobalSwap</c> undoes the console's 8-in-16 byte order and
/// is off only for data that was already swapped. Formats with no endian swap in the header
/// (L8) are never swapped.
/// </summary>
public static class Converter
{
    public static byte[] ConvertToPngBytes(string inputFilePath, bool performGlobalSwap = true)
        => ConvertToPngBytes(File.ReadAllBytes(inputFilePath), isVolume: !Is2DPath(inputFilePath), performGlobalSwap);

    public static byte[] ConvertToPngBytes(Stream input, bool isVolume = false, bool performGlobalSwap = true)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        return ConvertToPngBytes(ms.ToArray(), isVolume, performGlobalSwap);
    }

    /// <summary>
    /// A volume yields only its first depth slice.
    /// </summary>
    public static byte[] ConvertToPngBytes(byte[] rawData, bool isVolume = false, bool performGlobalSwap = true)
    {
        var (rgba, width, height, _) = DecodeToRgba(rawData, is2D: !isVolume, performGlobalSwap);
        return EncodePng(rgba, width, height);
    }

    /// <summary>
    /// One PNG per depth slice of a volume texture, in depth order. For fur, slice 0 is the
    /// layer sampled at the skin and later slices thin out toward the shell tip.
    /// </summary>
    public static IReadOnlyList<byte[]> ConvertVolumeToPngSlices(byte[] rawData, bool performGlobalSwap = true)
    {
        var (rgba, width, height, depth) = DecodeToRgba(rawData, is2D: false, performGlobalSwap);
        int bytesPerSlice = width * height * 4;
        var slices = new List<byte[]>(depth);
        for (int z = 0; z < depth; z++)
            slices.Add(EncodePng(new ReadOnlySpan<byte>(rgba, z * bytesPerSlice, bytesPerSlice), width, height));
        return slices;
    }

    /// <summary>
    /// Writes a 2D texture to <paramref name="outputFilePath"/>. A volume instead writes one
    /// "{name}_slice_NNN.png" file per depth slice beside it, plus an alpha-free "_RGB" copy of slice 0.
    /// Returns the number of slices written.
    /// </summary>
    public static int ConvertAndSave(string inputFilePath, string outputFilePath, bool performGlobalSwap = true)
    {
        bool is2D = Is2DPath(inputFilePath);
        var (rgba, width, height, depth) = DecodeToRgba(File.ReadAllBytes(inputFilePath), is2D, performGlobalSwap);
        return SaveSlices(rgba, width, height, depth, outputFilePath, is2D);
    }

    private static bool Is2DPath(string path) => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);

    private static (byte[] rgba, int width, int height, int depth) DecodeToRgba(byte[] rawData, bool is2D, bool performGlobalSwap)
    {
        GraphicFormat textureFormat = Header.ReadFormat(rawData);

        int pixelWidth, pixelHeight, pixelDepth;
        if (is2D)
        {
            (pixelWidth, pixelHeight) = Header.Read2DSize(rawData);
            pixelDepth = 1;
        }
        else
        {
            (pixelWidth, pixelDepth) = Header.ReadVolumeSize(rawData);
            pixelHeight = pixelWidth;
        }

        byte[] textureData = rawData[Header.HeaderSize..];
        if (performGlobalSwap && Header.IsByteSwapped(textureFormat))
            textureData = Deswizzler.SwapByteOrderX360(textureData);

        byte[] linearData = is2D
            ? Deswizzler.ConvertToLinearTexture(textureData, pixelWidth, pixelHeight, textureFormat)
            : Deswizzler.ConvertToLinearTexture3D(textureData, pixelWidth, pixelHeight, pixelDepth, textureFormat);

        return (ToRgba(linearData, pixelWidth, pixelHeight, pixelDepth, textureFormat), pixelWidth, pixelHeight, pixelDepth);
    }

    /// <summary>
    /// A8R8G8B8 passes through unconverted. Every other non-block format expands per pixel, so
    /// the pixel count comes from the linear data and covers all depth slices.
    /// </summary>
    private static byte[] ToRgba(byte[] linearData, int width, int height, int depth, GraphicFormat format)
    {
        switch (format)
        {
            case GraphicFormat.TextureFormatDXT1:
            case GraphicFormat.TextureFormatDXT3:
            case GraphicFormat.TextureFormatDXT5:
            case GraphicFormat.TextureFormatDXN:
                return DXTDecompressor.DecompressDXTTexture(linearData, width, height, depth, format);

            case GraphicFormat.TextureFormatA8R8G8B8:
                return linearData;

            case GraphicFormat.TextureFormatA8L8:
                return ExpandLuminance(linearData, hasAlpha: true);

            case GraphicFormat.TextureFormatL8:
                return ExpandLuminance(linearData, hasAlpha: false);

            case GraphicFormat.TextureFormatR5G6B5:
                return Unpack16(linearData, PixelPacking.Unpack565);

            case GraphicFormat.TextureFormatX4R4G4B4:
                return Unpack16(linearData, PixelPacking.Unpack444);

            default:
                throw new InvalidDataException($"Cannot process pixel format: {format}");
        }
    }

    private static byte[] Unpack16(byte[] linearData, Func<ushort, (byte R, byte G, byte B)> unpack)
    {
        byte[] rgba = new byte[linearData.Length / 2 * 4];
        for (int i = 0, dest = 0; dest < rgba.Length; i += 2, dest += 4)
        {
            var (r, g, b) = unpack(BitConverter.ToUInt16(linearData, i));
            rgba[dest] = r;
            rgba[dest + 1] = g;
            rgba[dest + 2] = b;
            rgba[dest + 3] = 255;
        }
        return rgba;
    }

    /// <summary>
    /// Luminance into R, G and B. A8L8 stores luminance then alpha, L8 luminance alone.
    /// </summary>
    private static byte[] ExpandLuminance(byte[] linearData, bool hasAlpha)
    {
        int stride = hasAlpha ? 2 : 1;
        byte[] rgba = new byte[linearData.Length / stride * 4];
        for (int src = 0, dest = 0; dest < rgba.Length; src += stride, dest += 4)
        {
            byte l = linearData[src];
            rgba[dest] = l;
            rgba[dest + 1] = l;
            rgba[dest + 2] = l;
            rgba[dest + 3] = hasAlpha ? linearData[src + 1] : (byte)255;
        }
        return rgba;
    }

    private static byte[] EncodePng(ReadOnlySpan<byte> rgba, int width, int height)
    {
        using var image = Image.LoadPixelData<Rgba32>(rgba, width, height);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static int SaveSlices(byte[] rgbaData, int width, int height, int depth, string outputPath, bool is2D)
    {
        int bytesPerSlice = width * height * 4;

        if (is2D)
        {
            SaveRgba(rgbaData, 0, bytesPerSlice, width, height, outputPath);
            return 1;
        }

        string baseDir = Path.GetDirectoryName(outputPath) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(outputPath);
        int slicesSaved = 0;

        for (int z = 0; z < depth; z++)
        {
            int startByte = z * bytesPerSlice;
            if (startByte + bytesPerSlice > rgbaData.Length) break;

            if (z == 0)
                SaveRgb(rgbaData, startByte, bytesPerSlice, width, height, Path.Combine(baseDir, $"{baseName}_slice_{z:D3}_RGB.png"));

            SaveRgba(rgbaData, startByte, bytesPerSlice, width, height, Path.Combine(baseDir, $"{baseName}_slice_{z:D3}.png"));
            slicesSaved++;
        }

        return slicesSaved;
    }

    private static void SaveRgba(byte[] data, int offset, int length, int width, int height, string filePath)
    {
        using var image = Image.LoadPixelData<Rgba32>(new ReadOnlySpan<byte>(data, offset, length), width, height);
        image.SaveAsPng(filePath);
    }

    private static void SaveRgb(byte[] data, int offset, int length, int width, int height, string filePath)
    {
        byte[] rgbData = new byte[width * height * 3];
        for (int i = 0, j = 0; i < length; i += 4, j += 3)
        {
            rgbData[j] = data[offset + i];
            rgbData[j + 1] = data[offset + i + 1];
            rgbData[j + 2] = data[offset + i + 2];
        }

        using var image = Image.LoadPixelData<Rgb24>(rgbData, width, height);
        image.SaveAsPng(filePath);
    }
}
