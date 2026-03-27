/**
 * @file        Formats/IPK/LzssDecoder.cs
 * @brief       LZSS decompression (lzss0 variant used by IPK archives)
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */

namespace ShadowForge.Formats.IPK;

public static class LzssDecoder
{
    private const int WindowSize = 4096;
    private const int MaxMatchLen = 18;
    private const int MinMatchLen = 2;
    private const int WindowFill = 0x20;

    public static byte[] Decompress(byte[] input, int outputSize)
    {
        var output = new byte[outputSize];
        var window = new byte[WindowSize];
        Array.Fill(window, (byte)WindowFill);

        int srcPos = 0;
        int dstPos = 0;
        int winPos = WindowSize - MaxMatchLen;
        int flags = 0;
        int flagBits = 0;

        while (dstPos < outputSize && srcPos < input.Length)
        {
            if (flagBits == 0)
            {
                if (srcPos >= input.Length) break;
                flags = input[srcPos++];
                flagBits = 8;
            }

            if ((flags & 1) != 0)
            {
                if (srcPos >= input.Length) break;
                byte b = input[srcPos++];
                output[dstPos++] = b;
                window[winPos] = b;
                winPos = (winPos + 1) & (WindowSize - 1);
            }
            else
            {
                if (srcPos + 1 >= input.Length) break;
                int lo = input[srcPos++];
                int hi = input[srcPos++];
                int offset = lo | ((hi & 0xF0) << 4);
                int length = (hi & 0x0F) + MinMatchLen;

                for (int i = 0; i < length && dstPos < outputSize; i++)
                {
                    byte b = window[(offset + i) & (WindowSize - 1)];
                    output[dstPos++] = b;
                    window[winPos] = b;
                    winPos = (winPos + 1) & (WindowSize - 1);
                }
            }

            flags >>= 1;
            flagBits--;
        }

        return output;
    }
}
