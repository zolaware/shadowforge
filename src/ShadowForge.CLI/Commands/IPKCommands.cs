using System.CommandLine;
using Microsoft.Extensions.Logging;
using ShadowForge.Formats.IPK;

namespace ShadowForge.CLI.Commands;

public static class IPKCommands
{
    public static Command Create(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var cmd = new Command("ipk", "IPK archive operations.");
        cmd.Subcommands.Add(BuildListCommand(getFactory));
        cmd.Subcommands.Add(BuildExtractCommand(getFactory));
        cmd.Subcommands.Add(BuildPackCommand(getFactory));
        return cmd;
    }

    private static Command BuildListCommand(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "IPK archive file." };
        var cmd = new Command("list", "List the contents of an IPK archive.");
        cmd.Arguments.Add(fileArg);
        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("ipk.list");
            var file = pr.GetValue(fileArg)!;
            try
            {
                using var fs = File.OpenRead(file.FullName);
                var archive = ArchiveReader.ReadArchive(fs);

                Console.WriteLine($"{archive.FileCount} files, {archive.ArchiveSize} bytes total");
                Console.WriteLine($"Compression: {(archive.UsesZlib ? "zlib" : "LZSS")}");
                Console.WriteLine();

                foreach (var entry in archive.Entries)
                {
                    string comp = entry.IsCompressed
                        ? $"compressed ({entry.CompressedSize}/{entry.OriginalSize})"
                        : $"stored ({entry.OriginalSize})";
                    Console.WriteLine($"  {entry.Name}  {comp}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to read IPK {File}", file.FullName);
                return 1;
            }
        });
        return cmd;
    }

    private static Command BuildExtractCommand(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "IPK archive file." };
        var outputOpt = new Option<DirectoryInfo?>("-o") { Description = "Output directory (default: a folder named after the archive beside it)." };
        var cmd = new Command("extract", "Extract every file from an IPK archive.");
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);
        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("ipk.extract");
            var file = pr.GetValue(fileArg)!;
            var outputDir = pr.GetValue(outputOpt)
                ?? new DirectoryInfo(Path.Combine(
                    Path.GetDirectoryName(file.FullName) ?? ".",
                    Path.GetFileNameWithoutExtension(file.FullName)));

            try
            {
                using var fs = File.OpenRead(file.FullName);
                var reader = new ArchiveReader(fs);
                reader.ExtractAll(outputDir.FullName);
                Console.WriteLine($"Extracted {reader.Archive.FileCount} files to {outputDir.FullName}");
                return 0;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to extract IPK {File}", file.FullName);
                return 1;
            }
        });
        return cmd;
    }

    private static Command BuildPackCommand(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "Original IPK archive to repack." };
        var folderArg = new Argument<DirectoryInfo>("folder") { Description = "Folder of replacement (and optional new) files. Relative paths must match internal archive paths (e.g. ply/pc03/pc03_01.dds)." };
        var outputOpt = new Option<FileInfo?>("-o") { Description = "Output IPK path (default: <original>_packed.ipk beside the original). Must not overwrite the original." };
        var noCompressOpt = new Option<bool>("--no-compress") { Description = "Store replacement/new data uncompressed." };
        var addNewOpt = new Option<bool>("--add-new") { Description = "Append folder files that do not match an existing entry as new archive entries." };

        var cmd = new Command("pack",
            "Repack an IPK by replacing matching entries from a folder. " +
            "Unchanged entries keep their original compressed data; matched files are zlib-compressed. " +
            "With --add-new, unmatched folder files are appended as new entries.");
        cmd.Arguments.Add(fileArg);
        cmd.Arguments.Add(folderArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(noCompressOpt);
        cmd.Options.Add(addNewOpt);

        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("ipk.pack");
            var file = pr.GetValue(fileArg)!;
            var folder = pr.GetValue(folderArg)!;
            bool noCompress = pr.GetValue(noCompressOpt);
            bool addNew = pr.GetValue(addNewOpt);

            string outputPath;
            if (pr.GetValue(outputOpt) is { } requested)
            {
                outputPath = requested.FullName;
            }
            else
            {
                string dir = Path.GetDirectoryName(file.FullName) ?? ".";
                string stem = Path.GetFileNameWithoutExtension(file.FullName);
                outputPath = Path.Combine(dir, stem + "_packed.ipk");
            }

            try
            {
                if (string.Equals(
                        Path.GetFullPath(outputPath),
                        Path.GetFullPath(file.FullName),
                        StringComparison.OrdinalIgnoreCase))
                {
                    log.LogError("Output must not overwrite the original IPK.");
                    return 1;
                }

                if (!file.Exists)
                {
                    log.LogError("Original IPK not found: {File}", file.FullName);
                    return 1;
                }

                if (!folder.Exists)
                {
                    log.LogError("Replacement folder not found: {Folder}", folder.FullName);
                    return 1;
                }

                byte[] original = File.ReadAllBytes(file.FullName);
                log.LogInformation(
                    "Repacking {File} with replacements from {Folder} (add-new={AddNew})",
                    file.FullName, folder.FullName, addNew);

                var result = ArchiveWriter.RepackFromFolder(
                    original,
                    folder.FullName,
                    compressReplacements: !noCompress,
                    addNewFiles: addNew);

                OutputPath.CreateParentDirectory(outputPath);
                File.WriteAllBytes(outputPath, result.Data);

                Console.WriteLine($"Replacement files found: {result.ReplacementCount}");
                Console.WriteLine($"Matching IPK entries:    {result.MatchedCount}");
                Console.WriteLine($"New entries added:       {result.AddedCount}");
                Console.WriteLine($"Unmatched (skipped):     {result.UnmatchedPaths.Count}");
                if (result.UnmatchedPaths.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("WARNING: These files do not match an IPK entry (use --add-new to append them):");
                    foreach (var name in result.UnmatchedPaths)
                        Console.WriteLine($"  {name}");
                }

                Console.WriteLine();
                Console.WriteLine("IPK REPACK COMPLETE");
                Console.WriteLine($"Original:     {file.FullName}");
                Console.WriteLine($"Output:       {outputPath}");
                Console.WriteLine($"Replaced:     {result.MatchedCount}");
                Console.WriteLine($"Added:        {result.AddedCount}");
                Console.WriteLine($"Archive size: {result.Data.Length:N0} bytes");
                return 0;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to pack IPK {File}", file.FullName);
                return 1;
            }
        });
        return cmd;
    }
}
