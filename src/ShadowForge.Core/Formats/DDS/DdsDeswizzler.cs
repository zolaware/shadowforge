using System;

namespace ShadowForge.Formats.DDS;

public static class DdsDeswizzler
{
    private const int TextureTileWidthHeight = 32;

    /// <summary>
    /// Performs a global 2-byte swap on the entire image data to convert from 
    /// Xbox 360's Big-Endian to Little-Endian.
    /// </summary>
    public static byte[] SwapByteOrderX360(byte[] imageData)
    {
        // Pad with a zero byte if odd length to avoid index out of bounds
        byte[] result = new byte[imageData.Length + (imageData.Length % 2)];
        Buffer.BlockCopy(imageData, 0, result, 0, imageData.Length);

        // Perform fast 2-byte swap
        for (int i = 0; i < result.Length - 1; i += 2)
        {
            byte temp = result[i];
            result[i] = result[i + 1];
            result[i + 1] = temp;
        }

        return result;
    }

    private static int GetLogBpp(int texelBytePitch)
    {
        return texelBytePitch switch
        {
            1 => 0,
            2 => 1,
            4 => 2,
            8 => 3,
            16 => 4,
            _ => (int)Math.Log2(texelBytePitch)
        };
    }

    private static int XgAddress2DTiledX(int blockOffset, int widthInBlocks, int texelBytePitch)
    {
        int alignedWidth = (widthInBlocks + 31) & ~31;
        int logBpp = GetLogBpp(texelBytePitch);

        int offsetByte = blockOffset << logBpp;
        int offsetTile = ((offsetByte & ~0xFFF) >> 3) + ((offsetByte & 0x700) >> 2) + (offsetByte & 0x3F);
        int offsetMacro = offsetTile >> (7 + logBpp);

        int macroX = ((offsetMacro % (alignedWidth >> 5)) << 2);
        int tile = ((((offsetTile >> (5 + logBpp)) & 2) + (offsetByte >> 6)) & 3);
        int macro = (macroX + tile) << 3;
        int micro = (((((offsetTile >> 1) & ~0xF) + (offsetTile & 0xF)) & ((texelBytePitch << 3) - 1))) >> logBpp;

        return macro + micro;
    }

    private static int XgAddress2DTiledY(int blockOffset, int widthInBlocks, int texelBytePitch)
    {
        int alignedWidth = (widthInBlocks + 31) & ~31;
        int logBpp = GetLogBpp(texelBytePitch);

        int offsetByte = blockOffset << logBpp;
        int offsetTile = ((offsetByte & ~0xFFF) >> 3) + ((offsetByte & 0x700) >> 2) + (offsetByte & 0x3F);
        int offsetMacro = offsetTile >> (7 + logBpp);

        int macroY = ((offsetMacro / (alignedWidth >> 5)) << 2);
        int tile = ((offsetTile >> (6 + logBpp)) & 1) + ((offsetByte & 0x800) >> 10);
        int macro = (macroY + tile) << 3;
        int micro = ((((offsetTile & (((texelBytePitch << 6) - 1) & ~0x1F)) + ((offsetTile & 0xF) << 1)) >> (3 + logBpp)) & ~1);

        return macro + micro + ((offsetTile & 0x10) >> 4);
    }

    private static int XgAddress3DTiledOffset(int x, int y, int z, int widthInBlocks, int heightInBlocks, int texelBytePitch)
    {
        int logBpp = GetLogBpp(texelBytePitch);

        int alignedWidth = (widthInBlocks + TextureTileWidthHeight - 1) & ~(TextureTileWidthHeight - 1);
        int alignedHeight = (heightInBlocks + TextureTileWidthHeight - 1) & ~(TextureTileWidthHeight - 1);

        int macroOuter = ((y >> 4) + (z >> 2) * (alignedHeight >> 4)) * (alignedWidth >> 5);
        int macro = ((((x >> 5) + macroOuter) << (logBpp + 6)) & 0xFFFFFFF) << 1;
        int micro = (((x & 7) + ((y & 6) << 2)) << (logBpp + 6)) >> 6;
        int offsetOuter = ((y >> 3) + (z >> 2)) & 1;
        int offset1 = offsetOuter + ((((x >> 3) + (offsetOuter << 1)) & 3) << 1);

        int offset2 = ((macro + (micro & ~15)) << 1) + (micro & 15) + ((z & 3) << (logBpp + 6)) + ((y & 1) << 4);

        int address = (offset1 & 1) << 3;
        address += (offset2 >> 6) & 7;
        address <<= 3;
        address += offset1 & ~1;
        address <<= 2;
        address += offset2 & ~511;
        address <<= 3;
        address += offset2 & 63;

        return address;
    }

    public static byte[] ConvertToLinearTexture(byte[] data, int pixelWidth, int pixelHeight, GraphicFormat format)
    {
        GetBlockPitchAndSize(format, out int blockPixelSize, out int texelBytePitch);

        int widthInBlocks = IsCompressedFormat(format) ? (int)Math.Ceiling(pixelWidth / 4.0) : pixelWidth / blockPixelSize;
        int heightInBlocks = IsCompressedFormat(format) ? (int)Math.Ceiling(pixelHeight / 4.0) : pixelHeight / blockPixelSize;

        int expectedLinearDataSize = widthInBlocks * heightInBlocks * texelBytePitch;
        byte[] destData = new byte[expectedLinearDataSize];

        int totalSwizzledBlocks = data.Length / texelBytePitch;

        for (int swizzledBlockIdx = 0; swizzledBlockIdx < totalSwizzledBlocks; swizzledBlockIdx++)
        {
            int srcByteOffset = swizzledBlockIdx * texelBytePitch;
            int linearX = XgAddress2DTiledX(swizzledBlockIdx, widthInBlocks, texelBytePitch);
            int linearY = XgAddress2DTiledY(swizzledBlockIdx, widthInBlocks, texelBytePitch);
            int destByteOffset = (linearY * widthInBlocks + linearX) * texelBytePitch;

            if (linearX < widthInBlocks && linearY < heightInBlocks &&
                destByteOffset + texelBytePitch <= expectedLinearDataSize &&
                srcByteOffset + texelBytePitch <= data.Length)
            {
                Buffer.BlockCopy(data, srcByteOffset, destData, destByteOffset, texelBytePitch);
            }
        }

        return destData;
    }

    public static byte[] ConvertToLinearTexture3D(byte[] data, int pixelWidth, int pixelHeight, int pixelDepth, GraphicFormat format)
    {
        GetBlockPitchAndSize(format, out int blockPixelSize, out int texelBytePitch);

        int widthInBlocks = IsCompressedFormat(format) ? (int)Math.Ceiling(pixelWidth / 4.0) : pixelWidth / blockPixelSize;
        int heightInBlocks = IsCompressedFormat(format) ? (int)Math.Ceiling(pixelHeight / 4.0) : pixelHeight / blockPixelSize;
        int depthInBlocks = IsCompressedFormat(format) ? pixelDepth : pixelDepth / blockPixelSize;

        int expectedLinearDataSize = widthInBlocks * heightInBlocks * depthInBlocks * texelBytePitch;
        byte[] destData = new byte[expectedLinearDataSize];

        for (int zLinear = 0; zLinear < depthInBlocks; zLinear++)
        {
            for (int yLinear = 0; yLinear < heightInBlocks; yLinear++)
            {
                for (int xLinear = 0; xLinear < widthInBlocks; xLinear++)
                {
                    int srcByteOffset = XgAddress3DTiledOffset(xLinear, yLinear, zLinear, widthInBlocks, heightInBlocks, texelBytePitch);
                    int destByteOffset = ((zLinear * heightInBlocks * widthInBlocks) + (yLinear * widthInBlocks) + xLinear) * texelBytePitch;

                    if (srcByteOffset >= 0 && srcByteOffset + texelBytePitch <= data.Length &&
                        destByteOffset >= 0 && destByteOffset + texelBytePitch <= expectedLinearDataSize)
                    {
                        Buffer.BlockCopy(data, srcByteOffset, destData, destByteOffset, texelBytePitch);
                    }
                }
            }
        }

        return destData;
    }

    public static void GetBlockPitchAndSize(GraphicFormat format, out int blockPixelSize, out int texelBytePitch)
    {
        switch (format)
        {
            case GraphicFormat.TextureFormatA8L8: blockPixelSize = 1; texelBytePitch = 2; break;
            case GraphicFormat.TextureFormatL8: blockPixelSize = 1; texelBytePitch = 1; break;
            case GraphicFormat.TextureFormatDxt1: blockPixelSize = 4; texelBytePitch = 8; break;
            case GraphicFormat.TextureFormatDxt3:
            case GraphicFormat.TextureFormatDxt5:
            case GraphicFormat.TextureFormatDxn: blockPixelSize = 4; texelBytePitch = 16; break;
            case GraphicFormat.TextureFormatA8R8G8B8: blockPixelSize = 1; texelBytePitch = 4; break;
            case GraphicFormat.TextureFormatX4R4G4B4: blockPixelSize = 1; texelBytePitch = 2; break;
            case GraphicFormat.TextureFormatR5G6B5: blockPixelSize = 1; texelBytePitch = 2; break;
            default: throw new ArgumentException($"Bad texture type: {format}");
        }
    }

    private static bool IsCompressedFormat(GraphicFormat format)
    {
        return format is GraphicFormat.TextureFormatDxt1 or
               GraphicFormat.TextureFormatDxt3 or
               GraphicFormat.TextureFormatDxt5 or
               GraphicFormat.TextureFormatDxn;
    }
}