namespace ShadowForge.IO;

public interface IArchiveReader
{
    IReadOnlyList<ArchiveEntry> Entries { get; }
    Stream OpenEntry(ArchiveEntry entry);
    void ExtractAll(string outputDir);
}

public record ArchiveEntry(string Name, long Size, long CompressedSize, bool IsCompressed);
