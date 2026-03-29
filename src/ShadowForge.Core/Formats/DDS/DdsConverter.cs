using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ShadowForge.Formats.DDS;

public static class DdsConverter
{
    public static int ConvertAndSave(string inputFilePath, string outputFilePath, bool performGlobalSwap = true)
    {
        const int headerOffset = 2048;

        byte[] rawData = File.ReadAllBytes(inputFilePath);

        // Header parsing logic identical to Python offsets
        byte txtFormat = rawData[39];
        GraphicFormat textureFormat = txtFormat switch
        {
            134 => GraphicFormat.TextureFormatA8R8G8B8,
            82 => GraphicFormat.TextureFormatDxt1,
            83 => GraphicFormat.TextureFormatDxt3,
            84 => GraphicFormat.TextureFormatDxt5,
            113 => GraphicFormat.TextureFormatDxn,
            74 => GraphicFormat.TextureFormatA8L8,
            79 => GraphicFormat.TextureFormatX4R4G4B4,
            68 => GraphicFormat.TextureFormatR5G6B5,
            2 => GraphicFormat.TextureFormatL8,
            _ => throw new Exception($"Unsupported texture format: {txtFormat}")
        };

        byte swz = rawData[32];
        int swzBlock = 128 * (swz - 128);

        byte p1 = rawData[41];
        byte p2 = rawData[42];
        byte p3 = rawData[43];

        bool is2D = inputFilePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);

        int pixelWidth, pixelHeight, pixelDepth;
        if (is2D)
        {
            pixelWidth = (int)Math.Round((p2 % 32) * 256.0 + p3 + 1);
            pixelHeight = (int)Math.Round(1 + p1 * 8.0 + p2 / 32.0);
            pixelDepth = 1;
        }
        else
        {
            pixelWidth = swzBlock;
            pixelHeight = swzBlock;
            pixelDepth = rawData[54];
        }

        byte[] textureData = new byte[rawData.Length - headerOffset];
        Buffer.BlockCopy(rawData, headerOffset, textureData, 0, textureData.Length);

        if (performGlobalSwap)
        {
            textureData = DdsDeswizzler.SwapByteOrderX360(textureData);
        }

        byte[] linearData = is2D
            ? DdsDeswizzler.ConvertToLinearTexture(textureData, pixelWidth, pixelHeight, textureFormat)
            : DdsDeswizzler.ConvertToLinearTexture3D(textureData, pixelWidth, pixelHeight, pixelDepth, textureFormat);

        byte[] finalPixelData = ProcessPixelFormat(linearData, pixelWidth, pixelHeight, pixelDepth, textureFormat);

        return SaveSlices(finalPixelData, pixelWidth, pixelHeight, pixelDepth, outputFilePath, is2D);
    }

    private static byte[] ProcessPixelFormat(byte[] linearData, int width, int height, int depth, GraphicFormat format)
    {
        switch (format)
        {
            case GraphicFormat.TextureFormatDxt1:
            case GraphicFormat.TextureFormatDxt3:
            case GraphicFormat.TextureFormatDxt5:
            case GraphicFormat.TextureFormatDxn:
                return DxtDecompressor.DecompressDxtTexture(linearData, width, height, depth, format);

            case GraphicFormat.TextureFormatA8R8G8B8:
            case GraphicFormat.TextureFormatA8L8:
            case GraphicFormat.TextureFormatL8:
                // Direct pass-through
                return linearData;

            case GraphicFormat.TextureFormatR5G6B5:
                byte[] rgbPixels = new byte[width * height * 4];
                for (int i = 0, dest = 0; i < linearData.Length - 1; i += 2, dest += 4)
                {
                    ushort pixel = BitConverter.ToUInt16(linearData, i);
                    rgbPixels[dest] = (byte)(((pixel >> 11) & 0x1F) * 255 / 31);    // R
                    rgbPixels[dest + 1] = (byte)(((pixel >> 5) & 0x3F) * 255 / 63); // G
                    rgbPixels[dest + 2] = (byte)((pixel & 0x1F) * 255 / 31);        // B
                    rgbPixels[dest + 3] = 255;                                      // A
                }
                return rgbPixels;

            case GraphicFormat.TextureFormatX4R4G4B4:
                byte[] rgbaPixels = new byte[width * height * 4];
                for (int i = 0, dest = 0; i < linearData.Length - 1; i += 2, dest += 4)
                {
                    ushort pixel = BitConverter.ToUInt16(linearData, i);
                    rgbaPixels[dest] = (byte)(((pixel >> 8) & 0xF) * 255 / 15);   // R
                    rgbaPixels[dest + 1] = (byte)(((pixel >> 4) & 0xF) * 255 / 15); // G
                    rgbaPixels[dest + 2] = (byte)((pixel & 0xF) * 255 / 15);        // B
                    rgbaPixels[dest + 3] = 255;                                     // A
                }
                return rgbaPixels;

            default:
                throw new Exception($"Cannot process pixel format: {format}");
        }
    }

    private static int SaveSlices(byte[] rgbaData, int width, int height, int depth, string outputPath, bool is2D)
    {
        int bytesPerSlice = width * height * 4;
        int slicesSaved = 0;

        if (is2D)
        {
            SaveBitmapRgba(rgbaData, 0, bytesPerSlice, width, height, outputPath);
            return 1;
        }

        string baseDir = Path.GetDirectoryName(outputPath) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(outputPath);

        for (int z = 0; z < Math.Min(4, depth); z++) // Python caps at 4 slices
        {
            int startByte = z * bytesPerSlice;
            if (startByte + bytesPerSlice > rgbaData.Length) break;

            if (z == 0)
            {
                // Save the first slice as RGB (no transparency) to match the Python script's logic
                string rgbPath = Path.Combine(baseDir, $"{baseName}_slice_{z:D3}_RGB.png");
                SaveBitmapRgb(rgbaData, startByte, bytesPerSlice, width, height, rgbPath);
            }

            // Save the standard RGBA slice
            string rgbaPath = Path.Combine(baseDir, $"{baseName}_slice_{z:D3}.png");
            SaveBitmapRgba(rgbaData, startByte, bytesPerSlice, width, height, rgbaPath);
            slicesSaved++;
        }

        return slicesSaved;
    }

    private static void SaveBitmapRgba(byte[] data, int offset, int length, int width, int height, string filePath)
    {
        // ImageSharp can load directly from a ReadOnlySpan over our byte array
        var span = new ReadOnlySpan<byte>(data, offset, length);
        using var image = Image.LoadPixelData<Rgba32>(span, width, height);
        image.SaveAsPng(filePath);
    }

    private static void SaveBitmapRgb(byte[] data, int offset, int length, int width, int height, string filePath)
    {
        // Manually copy R, G, B bytes while stripping Alpha, identical to the Python script
        byte[] rgbData = new byte[width * height * 3];
        for (int i = 0, j = 0; i < length; i += 4, j += 3)
        {
            rgbData[j] = data[offset + i];         // R
            rgbData[j + 1] = data[offset + i + 1]; // G
            rgbData[j + 2] = data[offset + i + 2]; // B
        }

        var span = new ReadOnlySpan<byte>(rgbData);
        using var image = Image.LoadPixelData<Rgb24>(span, width, height);
        image.SaveAsPng(filePath);
    }
}