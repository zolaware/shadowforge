using ShadowForge.IO;

namespace ShadowForge.Formats.DDS;

/// <summary>
/// The 2048-byte header of an Xbox 360 texture: a container preamble at 0x00-0x1F, then a
/// Xenos texture fetch constant of six big-endian dwords at 0x20-0x37. Texture data follows it.
/// </summary>
public static class Header
{
    public const int HeaderSize = 2048;

    private const int PageSize = 4096;

    private const int FetchDword0 = 0x20;
    private const int FetchDword1 = 0x24;
    private const int FetchDword2 = 0x28;
    private const int FetchDword3 = 0x2C;
    private const int FetchDword4 = 0x30;
    private const int FetchDword5 = 0x34;

    /// <summary>
    /// Low byte of fetch dword 1: the Xenos format ID in bits 0-5, the endian swap in bits 6-7.
    /// </summary>
    private const int FormatByte = FetchDword1 + 3;

    private static readonly Dictionary<GraphicFormat, int> FormatToXenos = new()
    {
        { GraphicFormat.TextureFormatL8,       2 },
        { GraphicFormat.TextureFormatR5G6B5,   4 },
        { GraphicFormat.TextureFormatA8R8G8B8, 6 },
        { GraphicFormat.TextureFormatA8L8,     10 },
        { GraphicFormat.TextureFormatX4R4G4B4, 15 },
        { GraphicFormat.TextureFormatDXT1,     18 },
        { GraphicFormat.TextureFormatDXT3,     19 },
        { GraphicFormat.TextureFormatDXT5,     20 },
        { GraphicFormat.TextureFormatDXN,      49 },
    };

    private static readonly Dictionary<int, GraphicFormat> XenosToFormat =
        FormatToXenos.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>
    /// Xenos endian swap per format: 0 none, 1 8-in-16, 2 8-in-32.
    /// </summary>
    private static readonly Dictionary<GraphicFormat, int> FormatToEndianness = new()
    {
        { GraphicFormat.TextureFormatL8,       0 },
        { GraphicFormat.TextureFormatR5G6B5,   1 },
        { GraphicFormat.TextureFormatA8R8G8B8, 2 },
        { GraphicFormat.TextureFormatA8L8,     1 },
        { GraphicFormat.TextureFormatX4R4G4B4, 1 },
        { GraphicFormat.TextureFormatDXT1,     1 },
        { GraphicFormat.TextureFormatDXT3,     1 },
        { GraphicFormat.TextureFormatDXT5,     1 },
        { GraphicFormat.TextureFormatDXN,      1 },
    };

    public static byte[] Build(int width, int height, GraphicFormat format)
    {
        byte[] header = new byte[HeaderSize];

        BigEndian.WriteUInt32(header, 0x00, (uint)ComputeSwizzledDataSize(width, height, format));
        BigEndian.WriteUInt32(header, 0x04, 0x00000003);
        BigEndian.WriteUInt32(header, 0x08, 0x00000001);
        BigEndian.WriteUInt32(header, 0x18, 0xFFFF0000);
        BigEndian.WriteUInt32(header, 0x1C, 0xFFFF0000);

        BigEndian.WriteUInt32(header, FetchDword0, 2u | ((uint)ComputePitch(width, format) << 22) | (1u << 31));
        BigEndian.WriteUInt32(header, FetchDword1, (uint)XenosId(format) | ((uint)Endianness(format) << 6));
        BigEndian.WriteUInt32(header, FetchDword2, (uint)(width - 1) | ((uint)(height - 1) << 13));
        BigEndian.WriteUInt32(header, FetchDword3,
            format == GraphicFormat.TextureFormatA8R8G8B8 ? 0x00000C14u : 0x00000D10u);
        BigEndian.WriteUInt32(header, FetchDword4, 0);
        BigEndian.WriteUInt32(header, FetchDword5, 1u << 9);

        return header;
    }

    /// <summary>
    /// Tiled data size: both axes, in blocks or pixels, are padded to a multiple of 32, and
    /// the total to whole 4 KB pages. At 1 and 2 bytes per unit a 32-unit-wide tiled layout
    /// reaches past the padded rectangle but always stays inside the page padding.
    /// </summary>
    public static int ComputeSwizzledDataSize(int width, int height, GraphicFormat format)
    {
        int alignedW = Align.Up(format.UnitsAcross(width), 32);
        int alignedH = Align.Up(format.UnitsAcross(height), 32);
        return Align.Up(alignedW * alignedH * format.BytesPerUnit(), PageSize);
    }

    /// <summary>
    /// The fetch constant's pitch field: the padded row width in pixels, divided by 32.
    /// </summary>
    private static int ComputePitch(int width, GraphicFormat format)
    {
        int alignedUnits = (int)Align.Up(format.UnitsAcross(width), 32);
        return (format.IsBlockCompressed() ? alignedUnits * 4 : alignedUnits) >> 5;
    }

    private static int XenosId(GraphicFormat format)
        => FormatToXenos.TryGetValue(format, out int id)
            ? id
            : throw new ArgumentException($"No Xenos texture format mapping for {format}");

    /// <summary>
    /// Whether the texture data carries the console's byte swap. Only L8 is stored unswapped.
    /// </summary>
    internal static bool IsByteSwapped(GraphicFormat format) => Endianness(format) != 0;

    private static int Endianness(GraphicFormat format)
        => FormatToEndianness.TryGetValue(format, out int e)
            ? e
            : throw new ArgumentException($"No endianness mapping for {format}");

    /// <summary>
    /// Reads the format from the fetch constant's Xenos ID and dimensions from dword 2.
    /// The endian bits are not checked. dataSize is the preamble's first dword.
    /// </summary>
    public static (GraphicFormat format, int width, int height, int dataSize) Parse(byte[] headerData)
    {
        if (headerData == null || headerData.Length < HeaderSize)
            throw new ArgumentException($"Header data must be at least {HeaderSize} bytes.");

        int dataSize = (int)BigEndian.ReadUInt32(headerData, 0x00);

        int xenosFmt = (int)(BigEndian.ReadUInt32(headerData, FetchDword1) & 0x3F);
        if (!XenosToFormat.TryGetValue(xenosFmt, out GraphicFormat format))
            throw new ArgumentException($"Unknown Xenos texture format ID: {xenosFmt}");

        uint dword2 = BigEndian.ReadUInt32(headerData, FetchDword2);
        int width = (int)(dword2 & 0x1FFF) + 1;
        int height = (int)((dword2 >> 13) & 0x1FFF) + 1;

        return (format, width, height, dataSize);
    }

    /// <summary>
    /// The format named by the fetch constant, accepting only the ID and endian-swap pairs
    /// that <see cref="Build"/> writes.
    /// </summary>
    internal static GraphicFormat ReadFormat(byte[] data)
    {
        byte value = data[FormatByte];
        if (XenosToFormat.TryGetValue(value & 0x3F, out var format) &&
            FormatToEndianness[format] == value >> 6)
            return format;
        throw new InvalidDataException($"Unsupported texture format: {value}");
    }

    /// <summary>
    /// A 2D texture's size, read byte-wise from fetch dword 2. It drops the top two height bits
    /// and rounds the height, so it agrees with <see cref="Parse"/> only up to 4096 x 2048.
    /// </summary>
    internal static (int width, int height) Read2DSize(byte[] data)
    {
        byte p1 = data[FetchDword2 + 1];
        byte p2 = data[FetchDword2 + 2];
        byte p3 = data[FetchDword2 + 3];
        int width = (int)Math.Round((p2 % 32) * 256.0 + p3 + 1);
        int height = (int)Math.Round(1 + p1 * 8.0 + p2 / 32.0);
        return (width, height);
    }

    /// <summary>
    /// A volume texture's slice edge length and slice count, from fetch dword 2's 3D layout:
    /// width - 1 in bits 0-10, height - 1 in bits 11-21, depth - 1 in bits 22-31.
    /// </summary>
    internal static (int edge, int depth) ReadVolumeSize(byte[] data)
    {
        uint dword2 = BigEndian.ReadUInt32(data, FetchDword2);
        int width = (int)(dword2 & 0x7FF) + 1;
        int height = (int)((dword2 >> 11) & 0x7FF) + 1;
        int depth = (int)(dword2 >> 22) + 1;
        if (width != height)
            throw new InvalidDataException($"Volume slices are {width}x{height}; only square slices are supported.");
        return (width, depth);
    }
}
