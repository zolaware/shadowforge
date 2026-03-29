using System;

namespace ShadowForge.Formats.DDS;

public static class DxtDecompressor
{
    private static void UnpackColor565(ushort color16Bit, out byte r, out byte g, out byte b)
    {
        int r5 = (color16Bit >> 11) & 0x1F;
        int g6 = (color16Bit >> 5) & 0x3F;
        int b5 = color16Bit & 0x1F;

        r = (byte)((r5 * 255) / 31);
        g = (byte)((g6 * 255) / 63);
        b = (byte)((b5 * 255) / 31);
    }

    private static byte[][] GenerateColorsDxt1(ushort color0, ushort color1)
    {
        UnpackColor565(color0, out byte r0, out byte g0, out byte b0);
        UnpackColor565(color1, out byte r1, out byte g1, out byte b1);

        byte[][] colors = new byte[4][];
        colors[0] = new byte[] { r0, g0, b0, 255 };
        colors[1] = new byte[] { r1, g1, b1, 255 };

        if (color0 > color1)
        {
            colors[2] = new byte[] { (byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3), 255 };
            colors[3] = new byte[] { (byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3), 255 };
        }
        else
        {
            colors[2] = new byte[] { (byte)((r0 + r1) / 2), (byte)((g0 + g1) / 2), (byte)((b0 + b1) / 2), 255 };
            colors[3] = new byte[] { 0, 0, 0, 0 };
        }

        return colors;
    }

    private static byte[] GenerateAlphasDxt5(byte alpha0, byte alpha1)
    {
        byte[] alphas = new byte[8];
        alphas[0] = alpha0;
        alphas[1] = alpha1;

        if (alpha0 > alpha1)
        {
            for (int i = 1; i < 7; i++)
                alphas[i + 1] = (byte)(((8 - i) * alpha0 + i * alpha1) / 8);
        }
        else
        {
            for (int i = 1; i < 5; i++)
                alphas[i + 1] = (byte)(((6 - i) * alpha0 + i * alpha1) / 6);
            alphas[6] = 0;
            alphas[7] = 255;
        }

        return alphas;
    }

    private static byte[] DecompressDxt1Block(ReadOnlySpan<byte> blockData)
    {
        ushort color0 = BitConverter.ToUInt16(blockData.Slice(0, 2));
        ushort color1 = BitConverter.ToUInt16(blockData.Slice(2, 2));
        uint colorIndices = BitConverter.ToUInt32(blockData.Slice(4, 4));

        byte[][] colors = GenerateColorsDxt1(color0, color1);
        byte[] pixels = new byte[64];

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int index = (int)((colorIndices >> ((y * 4 + x) * 2)) & 0x3);
                int idx = (y * 4 + x) * 4;
                pixels[idx] = colors[index][0];
                pixels[idx + 1] = colors[index][1];
                pixels[idx + 2] = colors[index][2];
                pixels[idx + 3] = colors[index][3];
            }
        }
        return pixels;
    }

    private static byte[] DecompressDxt3Block(ReadOnlySpan<byte> blockData)
    {
        ReadOnlySpan<byte> alphaData = blockData.Slice(0, 8);
        ReadOnlySpan<byte> colorBlockData = blockData.Slice(8, 8);

        byte[] alpha8BitValues = new byte[16];
        for (int i = 0; i < 8; i++)
        {
            byte val = alphaData[i];
            alpha8BitValues[i * 2] = (byte)((val & 0xF) * 17);
            alpha8BitValues[i * 2 + 1] = (byte)(((val >> 4) & 0xF) * 17);
        }

        ushort color0 = BitConverter.ToUInt16(colorBlockData.Slice(0, 2));
        ushort color1 = BitConverter.ToUInt16(colorBlockData.Slice(2, 2));
        uint colorIndices = BitConverter.ToUInt32(colorBlockData.Slice(4, 4));

        byte[][] colors = GenerateColorsDxt1(color0, color1);
        byte[] pixels = new byte[64];

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int colorIndex = (int)((colorIndices >> ((y * 4 + x) * 2)) & 0x3);
                int idx = (y * 4 + x) * 4;
                pixels[idx] = colors[colorIndex][0];
                pixels[idx + 1] = colors[colorIndex][1];
                pixels[idx + 2] = colors[colorIndex][2];
                pixels[idx + 3] = alpha8BitValues[y * 4 + x];
            }
        }
        return pixels;
    }

    private static byte[] DecompressDxt5Block(ReadOnlySpan<byte> blockData)
    {
        ReadOnlySpan<byte> alphaData = blockData.Slice(0, 8);
        ReadOnlySpan<byte> colorBlockData = blockData.Slice(8, 8);

        byte[] alphas = GenerateAlphasDxt5(alphaData[0], alphaData[1]);
        ulong alphaIndicesRaw = 0;
        for (int i = 0; i < 6; i++)
            alphaIndicesRaw |= (ulong)alphaData[2 + i] << (i * 8);

        ushort color0 = BitConverter.ToUInt16(colorBlockData.Slice(0, 2));
        ushort color1 = BitConverter.ToUInt16(colorBlockData.Slice(2, 2));
        uint colorIndices = BitConverter.ToUInt32(colorBlockData.Slice(4, 4));

        byte[][] colors = GenerateColorsDxt1(color0, color1);
        byte[] pixels = new byte[64];

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int pixelIndex = y * 4 + x;
                int colorIndex = (int)((colorIndices >> (pixelIndex * 2)) & 0x3);

                int alphaIdxShift = pixelIndex * 3;
                int alphaPixelIndex = (int)((alphaIndicesRaw >> alphaIdxShift) & 0x7);

                int idx = pixelIndex * 4;
                pixels[idx] = colors[colorIndex][0];
                pixels[idx + 1] = colors[colorIndex][1];
                pixels[idx + 2] = colors[colorIndex][2];
                pixels[idx + 3] = alphas[alphaPixelIndex];
            }
        }
        return pixels;
    }

    private static byte[] DecompressDxnBlock(ReadOnlySpan<byte> blockData)
    {
        ReadOnlySpan<byte> redChannelData = blockData.Slice(0, 8);
        ReadOnlySpan<byte> greenChannelData = blockData.Slice(8, 8);

        byte[] redValues = GenerateAlphasDxt5(redChannelData[0], redChannelData[1]);
        ulong redIndicesRaw = 0;
        for (int i = 0; i < 6; i++) redIndicesRaw |= (ulong)redChannelData[2 + i] << (i * 8);

        byte[] greenValues = GenerateAlphasDxt5(greenChannelData[0], greenChannelData[1]);
        ulong greenIndicesRaw = 0;
        for (int i = 0; i < 6; i++) greenIndicesRaw |= (ulong)greenChannelData[2 + i] << (i * 8);

        byte[] pixels = new byte[64];

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int pixelIndex = y * 4 + x;

                int redPixelIndex = (int)((redIndicesRaw >> (pixelIndex * 3)) & 0x7);
                byte r = redValues[redPixelIndex];

                int greenPixelIndex = (int)((greenIndicesRaw >> (pixelIndex * 3)) & 0x7);
                byte g = greenValues[greenPixelIndex];

                double normR = (r / 255.0) * 2.0 - 1.0;
                double normG = (g / 255.0) * 2.0 - 1.0;
                double dotProductSq = normR * normR + normG * normG;

                byte b = 0;
                if (dotProductSq <= 1.0)
                {
                    double normB = Math.Sqrt(1.0 - dotProductSq);
                    b = (byte)((normB * 0.5 + 0.5) * 255);
                }

                int idx = pixelIndex * 4;
                pixels[idx] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 255;
            }
        }
        return pixels;
    }

    public static byte[] DecompressDxtTexture(byte[] compressedData, int pixelWidth, int pixelHeight, int pixelDepth, GraphicFormat format)
    {
        int blockSizeBytes = format == GraphicFormat.TextureFormatDxt1 ? 8 : 16;
        Func<ReadOnlySpan<byte>, byte[]> decompressBlockFunc = format switch
        {
            GraphicFormat.TextureFormatDxt1 => DecompressDxt1Block,
            GraphicFormat.TextureFormatDxt3 => DecompressDxt3Block,
            GraphicFormat.TextureFormatDxt5 => DecompressDxt5Block,
            GraphicFormat.TextureFormatDxn => DecompressDxnBlock,
            _ => throw new ArgumentException("Unsupported DXT format.")
        };

        int paddedWidth = ((pixelWidth + 3) / 4) * 4;
        int paddedHeight = ((pixelHeight + 3) / 4) * 4;
        int paddedDepth = pixelDepth > 1 ? ((pixelDepth + 3) / 4) * 4 : 1;

        int numBlocksX = paddedWidth / 4;
        int numBlocksY = paddedHeight / 4;
        int numBlocksZ = paddedDepth;

        byte[] outputPixels = new byte[paddedWidth * paddedHeight * paddedDepth * 4];

        for (int blockZ = 0; blockZ < numBlocksZ; blockZ++)
        {
            for (int blockY = 0; blockY < numBlocksY; blockY++)
            {
                for (int blockX = 0; blockX < numBlocksX; blockX++)
                {
                    int compressedBlockOffset = ((blockZ * numBlocksY * numBlocksX) + (blockY * numBlocksX) + blockX) * blockSizeBytes;

                    if (compressedBlockOffset + blockSizeBytes <= compressedData.Length)
                    {
                        ReadOnlySpan<byte> blockData = new ReadOnlySpan<byte>(compressedData, compressedBlockOffset, blockSizeBytes);
                        byte[] decompressedBlock = decompressBlockFunc(blockData);

                        for (int yInBlock = 0; yInBlock < 4; yInBlock++)
                        {
                            for (int xInBlock = 0; xInBlock < 4; xInBlock++)
                            {
                                int destX = blockX * 4 + xInBlock;
                                int destY = blockY * 4 + yInBlock;
                                int destZ = blockZ;

                                int destIdx = ((destZ * paddedHeight * paddedWidth) + (destY * paddedWidth) + destX) * 4;
                                int srcIdx = (yInBlock * 4 + xInBlock) * 4;

                                if (destIdx + 3 < outputPixels.Length)
                                {
                                    outputPixels[destIdx] = decompressedBlock[srcIdx];
                                    outputPixels[destIdx + 1] = decompressedBlock[srcIdx + 1];
                                    outputPixels[destIdx + 2] = decompressedBlock[srcIdx + 2];
                                    outputPixels[destIdx + 3] = decompressedBlock[srcIdx + 3];
                                }
                            }
                        }
                    }
                }
            }
        }

        if (paddedWidth != pixelWidth || paddedHeight != pixelHeight || paddedDepth != pixelDepth)
        {
            byte[] croppedOutput = new byte[pixelWidth * pixelHeight * pixelDepth * 4];
            for (int z = 0; z < pixelDepth; z++)
            {
                for (int y = 0; y < pixelHeight; y++)
                {
                    int srcStart = (z * paddedHeight * paddedWidth * 4) + (y * paddedWidth * 4);
                    int destStart = (z * pixelHeight * pixelWidth * 4) + (y * pixelWidth * 4);
                    Buffer.BlockCopy(outputPixels, srcStart, croppedOutput, destStart, pixelWidth * 4);
                }
            }
            return croppedOutput;
        }

        return outputPixels;
    }
}