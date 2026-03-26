// P:\shadowforge\tests\ShadowForge.Tests\IO\BigEndianTests.cs
using ShadowForge.IO;

namespace ShadowForge.Tests.IO;

public class BigEndianReaderTests
{
    [Fact]
    public void ReadUInt16_BigEndian()
    {
        var data = new byte[] { 0x12, 0x34 };
        using var reader = new BigEndianReader(new MemoryStream(data));
        Assert.Equal((ushort)0x1234, reader.ReadUInt16());
    }

    [Fact]
    public void ReadUInt32_BigEndian()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        using var reader = new BigEndianReader(new MemoryStream(data));
        Assert.Equal(0xDEADBEEFu, reader.ReadUInt32());
    }

    [Fact]
    public void ReadFloat_BigEndian()
    {
        // 1.0f in big-endian = 0x3F800000
        var data = new byte[] { 0x3F, 0x80, 0x00, 0x00 };
        using var reader = new BigEndianReader(new MemoryStream(data));
        Assert.Equal(1.0f, reader.ReadFloat());
    }

    [Fact]
    public void ReadFixedString_ShiftJIS_NullTerminated()
    {
        // "ABC" + null padding to 8 bytes
        var data = new byte[] { 0x41, 0x42, 0x43, 0x00, 0x00, 0x00, 0x00, 0x00 };
        using var reader = new BigEndianReader(new MemoryStream(data));
        Assert.Equal("ABC", reader.ReadAsciiString(8));
    }

    [Fact]
    public void Seek_SetsPosition()
    {
        var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0xAB };
        using var reader = new BigEndianReader(new MemoryStream(data));
        reader.Seek(4);
        Assert.Equal(4, reader.Position);
        Assert.Equal((byte)0xAB, reader.ReadByte());
    }
}

public class BigEndianWriterTests
{
    [Fact]
    public void WriteUInt16_BigEndian()
    {
        using var ms = new MemoryStream();
        using var writer = new BigEndianWriter(ms, leaveOpen: true);
        writer.WriteUInt16(0x1234);
        writer.Flush();
        Assert.Equal(new byte[] { 0x12, 0x34 }, ms.ToArray());
    }

    [Fact]
    public void WriteUInt32_BigEndian()
    {
        using var ms = new MemoryStream();
        using var writer = new BigEndianWriter(ms, leaveOpen: true);
        writer.WriteUInt32(0xDEADBEEF);
        writer.Flush();
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, ms.ToArray());
    }

    [Fact]
    public void WriteFloat_BigEndian()
    {
        using var ms = new MemoryStream();
        using var writer = new BigEndianWriter(ms, leaveOpen: true);
        writer.WriteFloat(1.0f);
        writer.Flush();
        Assert.Equal(new byte[] { 0x3F, 0x80, 0x00, 0x00 }, ms.ToArray());
    }

    [Fact]
    public void WriteFixedBytes_PadsWithZeros()
    {
        using var ms = new MemoryStream();
        using var writer = new BigEndianWriter(ms, leaveOpen: true);
        writer.WriteFixedBytes(new byte[] { 0xAA, 0xBB }, 4);
        writer.Flush();
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0x00, 0x00 }, ms.ToArray());
    }

    [Fact]
    public void RoundTrip_UInt32()
    {
        using var ms = new MemoryStream();
        using (var writer = new BigEndianWriter(ms, leaveOpen: true))
        {
            writer.WriteUInt32(0x12345678);
            writer.Flush();
        }
        ms.Position = 0;
        using var reader = new BigEndianReader(ms);
        Assert.Equal(0x12345678u, reader.ReadUInt32());
    }
}
