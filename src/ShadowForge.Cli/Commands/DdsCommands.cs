using System.CommandLine;
// Assuming we will create the core logic in ShadowForge.Formats.DDS
using ShadowForge.Formats.DDS;

namespace ShadowForge.Cli.Commands;

public static class DdsCommands
{
    public static Command Create()
    {
        var cmd = new Command("dds", "Xbox 360 proprietary DDS texture operations");
        cmd.Subcommands.Add(BuildExportCommand());
        cmd.Subcommands.Add(BuildBatchExportCommand());
        return cmd;
    }

    private static Command BuildExportCommand()
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "Xbox 360 DDS texture file" };
        var outputOpt = new Option<FileInfo?>("-o") { Description = "Output .png file (acts as base name for 3D slices)" };
        var noSwapOpt = new Option<bool>("--no-swap") { Description = "Disable global 2-byte swap (endianness conversion)" };

        var cmd = new Command("export", "Export Xbox 360 DDS texture to PNG");
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(noSwapOpt);

        cmd.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var output = parseResult.GetValue(outputOpt)
                ?? new FileInfo(Path.ChangeExtension(file.FullName, ".png"));
            var noSwap = parseResult.GetValue(noSwapOpt);

            Console.WriteLine($"Reading and converting {file.Name}...");

            try
            {
                // We will implement DdsConverter in the Core library next
                int slicesSaved = DdsConverter.ConvertAndSave(file.FullName, output.FullName, !noSwap);

                if (slicesSaved == 1)
                    Console.WriteLine($"Done. Saved to {output.Name}.");
                else
                    Console.WriteLine($"Done. Saved {slicesSaved} slices based on {output.Name}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex.Message}");
            }
        });
        return cmd;
    }

    private static Command BuildBatchExportCommand()
    {
        var dirArg = new Argument<DirectoryInfo>("directory") { Description = "Directory containing .dds files" };
        var outputOpt = new Option<DirectoryInfo?>("-o") { Description = "Output directory" };
        var noSwapOpt = new Option<bool>("--no-swap") { Description = "Disable global 2-byte swap (endianness conversion)" };

        var cmd = new Command("batch-export", "Export all Xbox 360 DDS files in a directory to PNG");
        cmd.Arguments.Add(dirArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(noSwapOpt);

        cmd.SetAction(parseResult =>
        {
            var dir = parseResult.GetValue(dirArg)!;
            var outputDir = parseResult.GetValue(outputOpt) ?? dir;
            var noSwap = parseResult.GetValue(noSwapOpt);

            if (!outputDir.Exists) outputDir.Create();

            var ddsFiles = dir.GetFiles("*.dds", SearchOption.AllDirectories);
            var t36Files = dir.GetFiles("*.36t", SearchOption.AllDirectories);
            var files = ddsFiles.Concat(t36Files).ToArray();

            Console.WriteLine($"Found {files.Length} texture files ({ddsFiles.Length} DDS, {t36Files.Length} 36T).");

            int ok = 0, fail = 0;
            foreach (var file in files)
            {
                try
                {
                    string relPath = Path.GetRelativePath(dir.FullName, file.FullName);
                    string outPath = Path.Combine(outputDir.FullName, Path.ChangeExtension(relPath, ".png"));

                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                    DdsConverter.ConvertAndSave(file.FullName, outPath, !noSwap);
                    Console.WriteLine($"  OK: {file.Name}");
                    ok++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  FAIL: {file.Name} - {ex.Message}");
                    fail++;
                }
            }

            Console.WriteLine($"\nDone. {ok} exported, {fail} failed.");
        });
        return cmd;
    }
}