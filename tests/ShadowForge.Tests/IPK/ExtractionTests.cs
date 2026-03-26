// P:\shadowforge\tests\ShadowForge.Tests\Ipk\ExtractionTests.cs
using ShadowForge.Formats.IPK;

namespace ShadowForge.Tests.Ipk;

public class LzssDecoderTests
{
    [Fact]
    public void Decompress_LiteralBytes()
    {
        // Flag byte 0xFF = 8 literal bits
        // Followed by 8 literal bytes
        var input = new byte[] { 0xFF, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48 };
        var output = LzssDecoder.Decompress(input, 8);
        Assert.Equal("ABCDEFGH"u8.ToArray(), output);
    }

    [Fact]
    public void Decompress_EmptyInput_ReturnsZeros()
    {
        var output = LzssDecoder.Decompress(Array.Empty<byte>(), 4);
        Assert.Equal(4, output.Length);
        Assert.True(output.All(b => b == 0));
    }
}

public class EntryModelTests
{
    [Fact]
    public void Entry_Properties()
    {
        var entry = new Entry
        {
            Name = "test.rpj",
            IsCompressed = true,
            CompressedSize = 100,
            Offset = 0x1000,
            OriginalSize = 200,
        };

        Assert.Equal("test.rpj", entry.Name);
        Assert.True(entry.IsCompressed);
        Assert.Equal(100u, entry.CompressedSize);
    }
}
