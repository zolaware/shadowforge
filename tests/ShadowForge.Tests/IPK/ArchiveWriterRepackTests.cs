using System.Text;
using ShadowForge.Formats.IPK;

namespace ShadowForge.Tests.IPK;

public sealed class ArchiveWriterRepackTests
{
    [Fact]
    public void RepackFromFolder_ReplacesMatchedEntry_KeepsOthers()
    {
        var originalEntries = new[]
        {
            new ArchiveWriter.InputEntry("a/tex.dds", Encoding.ASCII.GetBytes("ORIGINAL_A")),
            new ArchiveWriter.InputEntry("b/model.hdb", Encoding.ASCII.GetBytes("ORIGINAL_B_DATA")),
        };
        byte[] original = ArchiveWriter.Build(originalEntries, alignment: 0x80);

        string folder = Path.Combine(Path.GetTempPath(), "ipk-repack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "a"));
            File.WriteAllBytes(Path.Combine(folder, "a", "tex.dds"), Encoding.ASCII.GetBytes("NEW_A_CONTENT"));

            var result = ArchiveWriter.RepackFromFolder(original, folder, compressReplacements: true);

            Assert.Equal(1, result.ReplacementCount);
            Assert.Equal(1, result.MatchedCount);
            Assert.Equal(0, result.AddedCount);
            Assert.Empty(result.UnmatchedPaths);

            using var ms = new MemoryStream(result.Data);
            var archive = ArchiveReader.ReadArchive(ms);
            Assert.Equal(2u, archive.FileCount);
            Assert.True(archive.UsesZlib);

            var e0 = archive.Entries[0];
            Assert.Equal("a/tex.dds", e0.Name);
            Assert.True(e0.IsCompressed);
            Assert.Equal((uint)Encoding.ASCII.GetByteCount("NEW_A_CONTENT"), e0.OriginalSize);

            using var out0 = new MemoryStream();
            ms.Position = 0;
            ArchiveReader.ExtractEntry(ms, e0, out0, useZlib: true);
            Assert.Equal("NEW_A_CONTENT", Encoding.ASCII.GetString(out0.ToArray()));

            var e1 = archive.Entries[1];
            Assert.Equal("b/model.hdb", e1.Name);
            using var out1 = new MemoryStream();
            ms.Position = 0;
            ArchiveReader.ExtractEntry(ms, e1, out1, useZlib: true);
            Assert.Equal("ORIGINAL_B_DATA", Encoding.ASCII.GetString(out1.ToArray()));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void RepackFromFolder_NoMatches_Throws()
    {
        var original = ArchiveWriter.Build(new[]
        {
            new ArchiveWriter.InputEntry("only.dds", new byte[] { 1, 2, 3 }),
        });

        string folder = Path.Combine(Path.GetTempPath(), "ipk-repack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "wrong.dds"), new byte[] { 9 });

            Assert.Throws<InvalidOperationException>(() =>
                ArchiveWriter.RepackFromFolder(original, folder));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void RepackFromFolder_UnmatchedReported_WhenNotAdding()
    {
        var original = ArchiveWriter.Build(new[]
        {
            new ArchiveWriter.InputEntry("keep.dds", new byte[] { 1 }),
        });

        string folder = Path.Combine(Path.GetTempPath(), "ipk-repack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "keep.dds"), new byte[] { 2 });
            File.WriteAllBytes(Path.Combine(folder, "extra.dds"), new byte[] { 3 });

            var result = ArchiveWriter.RepackFromFolder(original, folder);
            Assert.Equal(2, result.ReplacementCount);
            Assert.Equal(1, result.MatchedCount);
            Assert.Equal(0, result.AddedCount);
            Assert.Single(result.UnmatchedPaths);
            Assert.Equal("extra.dds", result.UnmatchedPaths[0]);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void RepackFromFolder_AddNew_AppendsUnmatchedEntries()
    {
        var original = ArchiveWriter.Build(new[]
        {
            new ArchiveWriter.InputEntry("ply\\keep.dds", Encoding.ASCII.GetBytes("KEEP")),
        }, alignment: 0x80);

        string folder = Path.Combine(Path.GetTempPath(), "ipk-repack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "ply"));
            Directory.CreateDirectory(Path.Combine(folder, "new"));
            File.WriteAllBytes(Path.Combine(folder, "ply", "keep.dds"), Encoding.ASCII.GetBytes("REPLACED"));
            File.WriteAllBytes(Path.Combine(folder, "new", "extra.dds"), Encoding.ASCII.GetBytes("EXTRA_DATA"));

            var result = ArchiveWriter.RepackFromFolder(
                original, folder, compressReplacements: true, addNewFiles: true);

            Assert.Equal(2, result.ReplacementCount);
            Assert.Equal(1, result.MatchedCount);
            Assert.Equal(1, result.AddedCount);
            Assert.Empty(result.UnmatchedPaths);

            using var ms = new MemoryStream(result.Data);
            var archive = ArchiveReader.ReadArchive(ms);
            Assert.Equal(2u, archive.FileCount);
            Assert.True(archive.UsesZlib);

            Assert.Equal("ply\\keep.dds", archive.Entries[0].Name);
            Assert.Equal("new\\extra.dds", archive.Entries[1].Name);

            using var out0 = new MemoryStream();
            ms.Position = 0;
            ArchiveReader.ExtractEntry(ms, archive.Entries[0], out0, useZlib: true);
            Assert.Equal("REPLACED", Encoding.ASCII.GetString(out0.ToArray()));

            using var out1 = new MemoryStream();
            ms.Position = 0;
            ArchiveReader.ExtractEntry(ms, archive.Entries[1], out1, useZlib: true);
            Assert.Equal("EXTRA_DATA", Encoding.ASCII.GetString(out1.ToArray()));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void RepackFromFolder_AddNewOnly_WithNoMatches_Succeeds()
    {
        var original = ArchiveWriter.Build(new[]
        {
            new ArchiveWriter.InputEntry("only.dds", new byte[] { 1, 2, 3 }),
        });

        string folder = Path.Combine(Path.GetTempPath(), "ipk-repack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "brand_new.dds"), new byte[] { 9, 8, 7 });

            var result = ArchiveWriter.RepackFromFolder(
                original, folder, compressReplacements: true, addNewFiles: true);

            Assert.Equal(0, result.MatchedCount);
            Assert.Equal(1, result.AddedCount);

            using var ms = new MemoryStream(result.Data);
            var archive = ArchiveReader.ReadArchive(ms);
            Assert.Equal(2u, archive.FileCount);
            Assert.Equal("only.dds", archive.Entries[0].Name);
            Assert.Equal("brand_new.dds", archive.Entries[1].Name);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
