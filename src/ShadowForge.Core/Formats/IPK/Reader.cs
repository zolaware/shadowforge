using System.IO.Compression;
using System.Text;
using ShadowForge.IO;

namespace ShadowForge.Formats.IPK;

public class Reader : IArchiveReader
{
    private const uint Magic = 0x314B5049; // "IPK1" as little-endian u32

    private readonly Stream _stream;
    private readonly Archive _archive;
    private readonly List<ArchiveEntry> _archiveEntries;

    public Reader(Stream stream)
    {
        _stream = stream;
        _archive = ReadArchive(stream);
        _archiveEntries = _archive.Entries.Select(e =>
            new ArchiveEntry(e.Name, e.OriginalSize, e.CompressedSize, e.IsCompressed)).ToList();
    }

    public Archive Archive => _archive;
    public IReadOnlyList<ArchiveEntry> Entries => _archiveEntries;

    public Stream OpenEntry(ArchiveEntry entry)
    {
        var ipkEntry = _archive.Entries.First(e => e.Name == entry.Name);
        var ms = new MemoryStream();
        ExtractEntry(_stream, ipkEntry, ms, _archive.UsesZlib);
        ms.Position = 0;
        return ms;
    }

    public void ExtractAll(string outputDir)
    {
        foreach (var entry in _archive.Entries)
        {
            string outPath = Path.Combine(outputDir, entry.Name.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            _stream.Seek(entry.Offset, SeekOrigin.Begin);

            if (!entry.IsCompressed)
            {
                using var outFile = File.Create(outPath);
                CopyBytes(_stream, outFile, (int)entry.OriginalSize);
            }
            else if (_archive.UsesZlib)
            {
                var compressedData = new byte[entry.CompressedSize];
                _stream.ReadExactly(compressedData);
                using var compStream = new MemoryStream(compressedData);
                compStream.ReadByte();
                compStream.ReadByte();
                using var deflate = new DeflateStream(compStream, CompressionMode.Decompress);
                using var outFile = File.Create(outPath);
                deflate.CopyTo(outFile);
            }
            else
            {
                var compressedData = new byte[entry.CompressedSize];
                _stream.ReadExactly(compressedData);
                var decompressed = LzssDecoder.Decompress(compressedData, (int)entry.OriginalSize);
                File.WriteAllBytes(outPath, decompressed);
            }
        }
    }

    public static Archive ReadArchive(Stream stream)
    {
        // IPK is little-endian — use standard BinaryReader, not BigEndianReader
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        uint magic = reader.ReadUInt32();
        if (magic != Magic)
            throw new InvalidDataException($"Not an IPK1 file (magic: 0x{magic:X8})");

        uint alignment = reader.ReadUInt32();
        uint fileCount = reader.ReadUInt32();
        uint archiveSize = reader.ReadUInt32();

        bool hasTimestamp = alignment == 0x80;

        var entries = new List<Entry>((int)fileCount);

        for (int i = 0; i < fileCount; i++)
        {
            var name = Encoding.ASCII.GetString(reader.ReadBytes(0x40)).TrimEnd('\0');
            uint compressed = reader.ReadUInt32();
            uint zsize = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            uint size = reader.ReadUInt32();

            if (i == 0 && !hasTimestamp && alignment != 0x800)
            {
                long savedPos = stream.Position;
                uint ts = reader.ReadUInt32();
                uint z1 = reader.ReadUInt32();
                uint z2 = reader.ReadUInt32();
                uint z3 = reader.ReadUInt32();
                if (z1 == 0 && z2 == 0 && z3 == 0)
                    hasTimestamp = true;
                stream.Seek(savedPos, SeekOrigin.Begin);
            }

            if (hasTimestamp)
                reader.ReadBytes(0x10);

            entries.Add(new Entry
            {
                Name = name,
                IsCompressed = compressed != 0,
                CompressedSize = zsize,
                Offset = offset,
                OriginalSize = size,
            });
        }

        return new Archive
        {
            Alignment = alignment,
            FileCount = fileCount,
            ArchiveSize = archiveSize,
            Entries = entries,
        };
    }

    public static void ExtractEntry(Stream stream, Entry entry, Stream output, bool useZlib)
    {
        stream.Seek(entry.Offset, SeekOrigin.Begin);

        if (!entry.IsCompressed)
        {
            CopyBytes(stream, output, (int)entry.OriginalSize);
        }
        else if (useZlib)
        {
            var compressedData = new byte[entry.CompressedSize];
            stream.ReadExactly(compressedData);
            using var compStream = new MemoryStream(compressedData);
            compStream.ReadByte();
            compStream.ReadByte();
            using var deflate = new DeflateStream(compStream, CompressionMode.Decompress);
            deflate.CopyTo(output);
        }
        else
        {
            var compressedData = new byte[entry.CompressedSize];
            stream.ReadExactly(compressedData);
            var decompressed = LzssDecoder.Decompress(compressedData, (int)entry.OriginalSize);
            output.Write(decompressed);
        }
    }

    private static void CopyBytes(Stream src, Stream dst, int count)
    {
        var buf = new byte[Math.Min(count, 81920)];
        int remaining = count;
        while (remaining > 0)
        {
            int toRead = Math.Min(remaining, buf.Length);
            int read = src.Read(buf, 0, toRead);
            if (read == 0) break;
            dst.Write(buf, 0, read);
            remaining -= read;
        }
    }
}
