using System.Buffers.Binary;
using ShadowForge.Text;

namespace ShadowForge.IO;

public class BigEndianReader : IDisposable
{
    private readonly BinaryReader _reader;

    public BigEndianReader(Stream stream, bool leaveOpen = false)
    {
        _reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen);
    }

    public Stream BaseStream => _reader.BaseStream;
    public long Position => _reader.BaseStream.Position;

    public void Seek(long offset) => _reader.BaseStream.Seek(offset, SeekOrigin.Begin);

    public byte ReadByte() => _reader.ReadByte();
    public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

    public ushort ReadUInt16()
        => BinaryPrimitives.ReadUInt16BigEndian(_reader.ReadBytes(2));

    public uint ReadUInt32()
        => BinaryPrimitives.ReadUInt32BigEndian(_reader.ReadBytes(4));

    public int ReadInt32()
        => BinaryPrimitives.ReadInt32BigEndian(_reader.ReadBytes(4));

    public float ReadFloat()
        => BitConverter.UInt32BitsToSingle(ReadUInt32());

    public string ReadShiftJISString(int length)
    {
        var bytes = _reader.ReadBytes(length);
        int end = Array.IndexOf(bytes, (byte)0);
        if (end < 0) end = length;
        return ShiftJisHelper.Encoding.GetString(bytes, 0, end);
    }

    public string ReadAsciiString(int length)
    {
        var bytes = _reader.ReadBytes(length);
        int end = Array.IndexOf(bytes, (byte)0);
        if (end < 0) end = length;
        return System.Text.Encoding.ASCII.GetString(bytes, 0, end);
    }

    public string ReadUnicodeString(int length)
    {
        var bytes = _reader.ReadBytes(length);
        int end = Array.IndexOf(bytes, (byte)0);
        if (end >= 0 && end + 1 < bytes.Length && bytes[end + 1] == 0)
            end &= ~1; // align to char boundary
        else if (end < 0) end = length;
        return System.Text.Encoding.Unicode.GetString(bytes, 0, end);
    }

    public byte[] ReadFixedBytes(int length) => _reader.ReadBytes(length);

    public void Dispose() => _reader.Dispose();
}
