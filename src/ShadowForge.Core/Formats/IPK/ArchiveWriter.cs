using System.IO.Compression;
using System.Text;
using ShadowForge.IO;

namespace ShadowForge.Formats.IPK;

public static class ArchiveWriter
{
    public sealed record InputEntry(string Name, byte[] Data);

    /// <summary>
    /// Builds an uncompressed IPK1 archive. Entry data starts at the aligned end of the entry
    /// table and every entry sits at an aligned offset with zero padding between. The record
    /// tail is written as zeros, so <see cref="ArchiveReader.ReadArchive"/> reports the result
    /// as zlib. That has no effect because no entry is compressed.
    /// </summary>
    public static byte[] Build(IReadOnlyList<InputEntry> entries, uint alignment = 0x80)
    {
        foreach (var entry in entries)
        {
            if (Encoding.ASCII.GetByteCount(entry.Name) > Archive.NameFieldSize - 1)
            {
                throw new ArgumentException(
                    $"Entry name '{entry.Name}' exceeds {Archive.NameFieldSize - 1} bytes", nameof(entries));
            }
        }

        uint tableEnd = (uint)(Archive.HeaderSize + Archive.EntryRecordSize * entries.Count);

        var offsets = new uint[entries.Count];
        uint cursor = Align.Up(tableEnd, alignment);
        for (int i = 0; i < entries.Count; i++)
        {
            cursor = Align.Up(cursor, alignment);
            offsets[i] = cursor;
            cursor += (uint)entries[i].Data.Length;
        }

        uint totalSize = Align.Up(cursor, alignment);

        using var ms = new MemoryStream((int)totalSize);
        using var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        writer.Write(Archive.Magic);
        writer.Write(alignment);
        writer.Write((uint)entries.Count);
        writer.Write(totalSize);

        for (int i = 0; i < entries.Count; i++)
        {
            var nameBytes = new byte[Archive.NameFieldSize];
            Encoding.ASCII.GetBytes(entries[i].Name, 0, entries[i].Name.Length, nameBytes, 0);
            writer.Write(nameBytes);

            uint size = (uint)entries[i].Data.Length;
            writer.Write(0u);
            writer.Write(size);
            writer.Write(offsets[i]);
            writer.Write(size);
            for (int k = 0; k < 4; k++)
                writer.Write(0u);
        }

        for (int i = 0; i < entries.Count; i++)
        {
            ms.Position = offsets[i];
            writer.Write(entries[i].Data);
        }

        ms.SetLength(totalSize);
        return ms.ToArray();
    }

    public sealed record RepackResult(
        byte[] Data,
        int ReplacementCount,
        int MatchedCount,
        int AddedCount,
        IReadOnlyList<string> UnmatchedPaths);

    /// <summary>
    /// Rebuilds an existing IPK1 archive, replacing any entries whose internal path matches a
    /// file under <paramref name="replacementFolder"/> (relative paths, case-insensitive,
    /// forward/backslash normalized). Unmatched original entries keep their original compressed
    /// payload byte-for-byte. Matched entries are zlib-compressed from the replacement file data.
    /// When <paramref name="addNewFiles"/> is true, folder files that do not match any existing
    /// entry are appended as new entries at the end of the archive. Alignment and original entry
    /// order are preserved. The output always uses a zlib-style entry tail so
    /// <see cref="Archive.UsesZlib"/> remains true.
    /// </summary>
    public static RepackResult RepackFromFolder(
        byte[] originalBytes,
        string replacementFolder,
        bool compressReplacements = true,
        bool addNewFiles = false)
    {
        if (!Directory.Exists(replacementFolder))
            throw new DirectoryNotFoundException($"Replacement folder not found: {replacementFolder}");

        using var originalStream = new MemoryStream(originalBytes, writable: false);
        var archive = ArchiveReader.ReadArchive(originalStream);

        var replacements = CollectReplacements(replacementFolder);
        if (replacements.Count == 0)
            throw new InvalidOperationException($"No replacement files found in {replacementFolder}");

        var archiveByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < archive.Entries.Count; i++)
            archiveByKey[NormalizePath(archive.Entries[i].Name)] = i;
		
		char pathSeparator = DetectPathSeparator(archive.Entries);

        var matched = new Dictionary<int, (string RelativePath, byte[] Data)>();
        var newFiles = new List<(string RelativePath, byte[] Data)>();
        var unmatched = new List<string>();

        foreach (var (key, (relative, data)) in replacements)
        {
            if (archiveByKey.TryGetValue(key, out int index))
            {
                matched[index] = (relative, data);
            }
            else if (addNewFiles)
            {
                string archiveName = ToArchivePath(relative, pathSeparator);
                if (Encoding.ASCII.GetByteCount(archiveName) > Archive.NameFieldSize - 1)
                {
                    throw new ArgumentException(
                        $"Entry name '{archiveName}' exceeds {Archive.NameFieldSize - 1} bytes");
                }
                newFiles.Add((archiveName, data));
            }
			else
            {
                unmatched.Add(relative);
            }
        }

        if (matched.Count == 0 && newFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "No replacement files matched the IPK and no new files were added. " +
                "Check the folder structure and internal filenames, or pass --add-new.");
        }

        int totalEntries = archive.Entries.Count + newFiles.Count;

        var names = new string[totalEntries];
        var blobs = new byte[totalEntries][];
        var isCompressed = new bool[totalEntries];
        var originalSizes = new uint[totalEntries];

        for (int i = 0; i < archive.Entries.Count; i++)
        {
            var entry = archive.Entries[i];
            names[i] = entry.Name;

            if (matched.TryGetValue(i, out var repl))
            {
                byte[] payload = compressReplacements
                    ? CompressZlib(repl.Data)
                    : repl.Data;
                blobs[i] = payload;
                isCompressed[i] = compressReplacements;
                originalSizes[i] = (uint)repl.Data.Length;
            }
            else
            {
                var blob = new byte[entry.CompressedSize];
                originalStream.Position = entry.Offset;
                originalStream.ReadExactly(blob);
                blobs[i] = blob;
                isCompressed[i] = entry.IsCompressed;
                originalSizes[i] = entry.OriginalSize;
            }
        }

        // Appended new files.
        for (int n = 0; n < newFiles.Count; n++)
        {
            int i = archive.Entries.Count + n;
            var (relative, data) = newFiles[n];
            names[i] = relative;
            byte[] payload = compressReplacements ? CompressZlib(data) : data;
            blobs[i] = payload;
            isCompressed[i] = compressReplacements;
            originalSizes[i] = (uint)data.Length;
        }

        uint alignment = archive.Alignment;
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            throw new InvalidDataException($"Invalid alignment: {alignment}");

        uint tableEnd = (uint)(Archive.HeaderSize + Archive.EntryRecordSize * totalEntries);
        uint dataStart = Align.Up(tableEnd, alignment);

        var offsets = new uint[totalEntries];
        uint cursor = dataStart;
        for (int i = 0; i < totalEntries; i++)
        {
            cursor = Align.Up(cursor, alignment);
            offsets[i] = cursor;
            cursor += (uint)blobs[i].Length;
        }

        uint totalSize = Align.Up(cursor, alignment);

        using var ms = new MemoryStream((int)totalSize);
        using var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        writer.Write(Archive.Magic);
        writer.Write(alignment);
        writer.Write((uint)totalEntries);
        writer.Write(totalSize);

        for (int i = 0; i < totalEntries; i++)
        {
            var nameBytes = new byte[Archive.NameFieldSize];
            Encoding.ASCII.GetBytes(names[i], 0, names[i].Length, nameBytes, 0);
            writer.Write(nameBytes);

            writer.Write(isCompressed[i] ? 1u : 0u);
            writer.Write((uint)blobs[i].Length);
            writer.Write(offsets[i]);
            writer.Write(originalSizes[i]);
            for (int k = 0; k < 4; k++)
                writer.Write(0u);
        }

        if (ms.Position > dataStart)
            throw new InvalidDataException("File table exceeds the data start offset");

        while (ms.Position < dataStart)
            writer.Write((byte)0);

        for (int i = 0; i < totalEntries; i++)
        {
            if (ms.Position > offsets[i])
                throw new InvalidDataException($"Overlapping data while writing {names[i]}");
            while (ms.Position < offsets[i])
                writer.Write((byte)0);
            writer.Write(blobs[i]);
        }

        while (ms.Position < totalSize)
            writer.Write((byte)0);

        ms.SetLength(totalSize);
        return new RepackResult(ms.ToArray(), replacements.Count, matched.Count, newFiles.Count, unmatched);
    }

    private static byte[] CompressZlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return ms.ToArray();
    }

    /// <summary>
    /// Collects every file under <paramref name="folder"/> keyed by its path relative to the
    /// folder root, normalized for case-insensitive matching against IPK internal names.
    /// </summary>
    private static Dictionary<string, (string RelativePath, byte[] Data)> CollectReplacements(string folder)
    {
        var result = new Dictionary<string, (string, byte[])>(StringComparer.OrdinalIgnoreCase);
        var root = Path.GetFullPath(folder);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            string key = NormalizePath(relative);
            if (result.ContainsKey(key))
                throw new InvalidOperationException($"Duplicate replacement path: {relative}");
            result[key] = (relative, File.ReadAllBytes(path));
        }

        return result;
    }

    private static string NormalizePath(string name) =>
        name.Replace('\\', '/').Trim('/').ToLowerInvariant();

    private static char DetectPathSeparator(IReadOnlyList<FileEntry> entries)
    {
        int backslash = 0, slash = 0;
        foreach (var entry in entries)
        {
            foreach (char c in entry.Name)
            {
                if (c == '\\') backslash++;
                else if (c == '/') slash++;
            }
        }
        if (slash > backslash)
            return '/';
        return '\\';
    }

    private static string ToArchivePath(string relativePath, char separator)
    {
        string normalized = relativePath.Replace('\\', '/').Trim('/');
        return separator == '/'
            ? normalized
            : normalized.Replace('/', '\\');
    }
}