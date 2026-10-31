using System.IO.Compression;
using System.Text;

namespace ShadowForge.Formats.IPK;

public sealed class ArchiveReader
{
    private readonly Stream _stream;

    public ArchiveReader(Stream stream)
    {
        _stream = stream;
        Archive = ReadArchive(stream);
    }

    public Archive Archive { get; }

    public void ExtractAll(string outputDir)
    {
        foreach (var entry in Archive.Entries)
        {
            string outPath = Path.Combine(outputDir, entry.Name.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            using var outFile = File.Create(outPath);
            ExtractEntry(_stream, entry, outFile, Archive.UsesZlib);
        }
    }

    public static Archive ReadArchive(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        uint magic = reader.ReadUInt32();
        if (magic != Archive.Magic)
            throw new InvalidDataException($"Not an IPK1 file (magic: 0x{magic:X8})");

        uint alignment = reader.ReadUInt32();
        uint fileCount = reader.ReadUInt32();
        uint archiveSize = reader.ReadUInt32();

        bool usesZlib = false;
        var entries = new List<FileEntry>((int)fileCount);

        for (int i = 0; i < fileCount; i++)
        {
            var name = Encoding.ASCII.GetString(reader.ReadBytes(Archive.NameFieldSize)).TrimEnd('\0');
            uint compressed = reader.ReadUInt32();
            uint compressedSize = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            uint size = reader.ReadUInt32();
            reader.ReadUInt32();
            uint tail0 = reader.ReadUInt32();
            uint tail1 = reader.ReadUInt32();
            uint tail2 = reader.ReadUInt32();

            if (i == 0 && tail0 == 0 && tail1 == 0 && tail2 == 0)
                usesZlib = true;

            entries.Add(new FileEntry
            {
                Name = name,
                IsCompressed = compressed != 0,
                CompressedSize = compressedSize,
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
            UsesZlib = usesZlib,
        };
    }

    public static void ExtractEntry(Stream stream, FileEntry entry, Stream output, bool useZlib)
    {
        stream.Seek(entry.Offset, SeekOrigin.Begin);

        if (!entry.IsCompressed)
        {
            var buffer = new byte[entry.OriginalSize];
            stream.ReadExactly(buffer);
            output.Write(buffer);
            return;
        }

        var compressedData = new byte[entry.CompressedSize];
        stream.ReadExactly(compressedData);
        if (useZlib)
        {
            using var zlib = new ZLibStream(new MemoryStream(compressedData), CompressionMode.Decompress);
            zlib.CopyTo(output);
        }
        else
        {
            output.Write(LZSSDecoder.Decompress(compressedData, (int)entry.OriginalSize));
        }
    }

    /// <summary>
    /// The archive table and every entry's decompressed bytes, in archive order.
    /// </summary>
    public static (Archive Archive, List<ArchiveWriter.InputEntry> Entries) ReadAllEntries(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        var archive = ReadArchive(stream);
        var entries = new List<ArchiveWriter.InputEntry>(archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            using var output = new MemoryStream();
            ExtractEntry(stream, entry, output, archive.UsesZlib);
            entries.Add(new ArchiveWriter.InputEntry(entry.Name, output.ToArray()));
        }
        return (archive, entries);
    }
}
