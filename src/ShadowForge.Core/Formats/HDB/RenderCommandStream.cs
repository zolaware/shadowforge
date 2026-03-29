/**
 * @file        Formats/HDB/RenderCommandStream.cs
 * @brief       HDB render command byte stream parser and writer
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */
using ShadowForge.IO;

namespace ShadowForge.Formats.HDB;

public static class RenderCommandStream
{
    private static readonly HashSet<byte> OneByteParamOpcodes = new()
    {
        1, 3, 4, 5, 6, 7, 8, 9, 10, 15, 17, 19, 22, 23, 26, 28, 29, 30,
        31, 34, 35, 39, 40, 43, 45, 57, 69, 79, 84, 89, 92, 97, 98,
        110, 120, 126, 131, 198, 215, 224,
    };

    private static readonly HashSet<byte> IaSelectOpcodes = new() { 0x10, 0x20, 0x30 };

    public static List<RenderCommand> Parse(byte[] data)
    {
        var commands = new List<RenderCommand>();
        int pos = 0;
        while (pos < data.Length)
        {
            byte opcode = data[pos++];
            byte[] paramData;

            if (opcode == 0x00)
            {
                // End or zero padding
                if (pos < data.Length)
                {
                    byte check = data[pos++];
                    if (check == 0xFF)
                        paramData = new[] { check };
                    else
                    {
                        paramData = new byte[5];
                        paramData[0] = check;
                        if (pos + 4 <= data.Length)
                        {
                            Array.Copy(data, pos, paramData, 1, 4);
                            pos += 4;
                        }
                    }
                }
                else
                    paramData = Array.Empty<byte>();
            }
            else if (opcode == 0x50) // Indicate
            {
                paramData = pos < data.Length ? new[] { data[pos++] } : Array.Empty<byte>();
            }
            else if (opcode == 0x40) // VA Select
            {
                paramData = new byte[3];
                if (pos + 3 <= data.Length) { Array.Copy(data, pos, paramData, 0, 3); pos += 3; }
            }
            else if (opcode == 0x02) // Matrix Palette
            {
                byte count = pos < data.Length ? data[pos++] : (byte)0;
                paramData = new byte[1 + count * 2];
                paramData[0] = count;
                if (pos + count * 2 <= data.Length) { Array.Copy(data, pos, paramData, 1, count * 2); pos += count * 2; }
            }
            else if (IaSelectOpcodes.Contains(opcode))
            {
                paramData = new byte[5];
                if (pos + 5 <= data.Length) { Array.Copy(data, pos, paramData, 0, 5); pos += 5; }
            }
            else if (opcode == 0x60) // Material Select
            {
                paramData = pos < data.Length ? new[] { data[pos++] } : Array.Empty<byte>();
            }
            else if (opcode == 0x93) // Diffuse/Specular RGB
            {
                paramData = new byte[3];
                if (pos + 3 <= data.Length) { Array.Copy(data, pos, paramData, 0, 3); pos += 3; }
            }
            else if (opcode == 0x94) // Section 2
            {
                paramData = new byte[5];
                if (pos + 5 <= data.Length) { Array.Copy(data, pos, paramData, 0, 5); pos += 5; }
            }
            else if (opcode == 0x19) // Section 1
            {
                paramData = new byte[4];
                if (pos + 4 <= data.Length) { Array.Copy(data, pos, paramData, 0, 4); pos += 4; }
            }
            else if (OneByteParamOpcodes.Contains(opcode))
            {
                paramData = pos < data.Length ? new[] { data[pos++] } : Array.Empty<byte>();
            }
            else
            {
                paramData = Array.Empty<byte>();
            }

            commands.Add(new RenderCommand { Opcode = opcode, Data = paramData });
        }
        return commands;
    }

    public static byte[] Write(List<RenderCommand> commands)
    {
        using var ms = new MemoryStream();
        foreach (var cmd in commands)
        {
            ms.WriteByte(cmd.Opcode);
            ms.Write(cmd.Data);
        }
        return ms.ToArray();
    }
}
