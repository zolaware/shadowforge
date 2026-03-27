using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ShadowForge.IO;

/// <summary>
/// Extension methods for reading/writing big-endian values from byte arrays and spans.
/// Centralizes all byte-swapping so no format code needs hand-rolled shifts.
/// </summary>
public static class BigEndian
{
    // --- Read from byte[] at offset ---

    public static ushort ReadUInt16(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

    public static uint ReadUInt32(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    public static short ReadInt16(byte[] data, int offset)
        => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));

    public static int ReadInt32(byte[] data, int offset)
        => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));

    public static float ReadFloat(byte[] data, int offset)
    {
        uint raw = ReadUInt32(data, offset);
        return BitConverter.UInt32BitsToSingle(raw);
    }

    // --- Write to byte[] at offset ---

    public static void WriteUInt16(byte[] data, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), value);

    public static void WriteUInt32(byte[] data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);

    public static void WriteInt32(byte[] data, int offset, int value)
        => BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, 4), value);

    public static void WriteFloat(byte[] data, int offset, float value)
    {
        uint raw = BitConverter.SingleToUInt32Bits(value);
        WriteUInt32(data, offset, raw);
    }
}
