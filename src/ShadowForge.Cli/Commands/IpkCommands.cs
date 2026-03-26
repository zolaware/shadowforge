using System.CommandLine;
using ShadowForge.Formats.IPK;

namespace ShadowForge.Cli.Commands;

public static class IpkCommands
{
    public static Command Create()
    {
        var cmd = new Command("ipk", "IPK archive operations");
        cmd.Subcommands.Add(BuildListCommand());
        cmd.Subcommands.Add(BuildExtractCommand());
        return cmd;
    }

    private static Command BuildListCommand()
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "IPK archive file" };
        var cmd = new Command("list", "List contents of an IPK archive");
        cmd.Arguments.Add(fileArg);
        cmd.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            using var fs = File.OpenRead(file.FullName);
            var archive = Reader.ReadArchive(fs);

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
        });
        return cmd;
    }

    private static Command BuildExtractCommand()
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "IPK archive file" };
        var outputOpt = new Option<DirectoryInfo?>("-o") { Description = "Output directory" };
        var cmd = new Command("extract", "Extract all files from an IPK archive");
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);
        cmd.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var outputDir = parseResult.GetValue(outputOpt)
                ?? new DirectoryInfo(Path.Combine(
                    Path.GetDirectoryName(file.FullName) ?? ".",
                    Path.GetFileNameWithoutExtension(file.FullName)));

            using var fs = File.OpenRead(file.FullName);
            var reader = new Reader(fs);
            reader.ExtractAll(outputDir.FullName);
            Console.WriteLine($"Extracted {reader.Archive.FileCount} files to {outputDir.FullName}");
        });
        return cmd;
    }
}
