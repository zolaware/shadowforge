using System.CommandLine;
using RPJ = ShadowForge.Formats.RPJ;
using BDS = ShadowForge.Formats.BDS;
using BDSL = ShadowForge.Formats.BDSL;

namespace ShadowForge.Cli.Commands;

public static class RpjCommands
{
    public static Command Create()
    {
        var cmd = new Command("rpj", "RPJ scene script operations");
        cmd.Subcommands.Add(BuildDecompileCommand());
        cmd.Subcommands.Add(BuildCompileCommand());
        cmd.Subcommands.Add(BuildVerifyCommand());
        cmd.Subcommands.Add(BuildBatchDecompileCommand());
        cmd.Subcommands.Add(BuildBatchCompileCommand());
        cmd.Subcommands.Add(BuildBatchVerifyCommand());
        return cmd;
    }

    private static Command BuildDecompileCommand()
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "RPJ file to decompile" };
        var outputOpt = new Option<FileInfo?>("-o") { Description = "Output file path" };
        var formatOpt = new Option<string>("--format") { Description = "Output format: bdsl (default) or bds", DefaultValueFactory = _ => "bdsl" };
        var cmd = new Command("decompile", "Decompile RPJ binary to text");
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(formatOpt);
        cmd.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var format = parseResult.GetValue(formatOpt) ?? "bdsl";
            string ext = format == "bds" ? ".bds" : ".bdsl";
            var output = parseResult.GetValue(outputOpt) ?? new FileInfo(Path.ChangeExtension(file.FullName, ext));

            var rpj = RPJ.Reader.Read(file.FullName);

            if (format == "bds")
                BDS.Writer.Write(rpj, output.FullName);
            else
                BDSL.Writer.Write(rpj, output.FullName);

            Console.WriteLine($"Written to {output.FullName}");
            Console.WriteLine($"  {rpj.Entries.Count} entries, {rpj.Scripts.Count} script blocks, {rpj.Waypoints.Count} waypoints");
        });
        return cmd;
    }

    private static Command BuildCompileCommand()
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "BDSL or BDS file to compile" };
        var outputOpt = new Option<FileInfo?>("-o") { Description = "Output RPJ file path" };
        var cmd = new Command("compile", "Compile BDSL/BDS text to RPJ binary");
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);
        cmd.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var output = parseResult.GetValue(outputOpt) ?? new FileInfo(Path.ChangeExtension(file.FullName, ".rpj"));

            RPJ.SceneFile rpj;
            if (file.Extension.Equals(".bds", StringComparison.OrdinalIgnoreCase))
                rpj = BDS.Parser.ParseFile(file.FullName);
            else
                rpj = BDSL.Parser.ParseFile(file.FullName);

            RPJ.Writer.Write(rpj, output.FullName);
            Console.WriteLine($"Written to {output.FullName}");
        });
        return cmd;
    }

    private static Command BuildVerifyCommand()
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "BDS file to verify" };
        var originalOpt = new Option<FileInfo?>("--original") { Description = "Original RPJ file for comparison" };
        var cmd = new Command("verify", "Verify byte-identical round-trip");
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(originalOpt);
        cmd.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var originalFile = parseResult.GetValue(originalOpt)
                ?? new FileInfo(Path.ChangeExtension(file.FullName, ".rpj"));

            if (!originalFile.Exists)
            {
                Console.Error.WriteLine($"Cannot find original RPJ: {originalFile.FullName}");
                return 1;
            }

            var rpj = BDS.Parser.ParseFile(file.FullName);
            var compiled = RPJ.Writer.Write(rpj);
            var original = File.ReadAllBytes(originalFile.FullName);

            if (compiled.Length != original.Length)
            {
                Console.WriteLine($"MISMATCH: size differs ({compiled.Length} vs {original.Length} bytes)");
                return 1;
            }

            int diffs = 0;
            for (int i = 0; i < compiled.Length; i++)
            {
                if (compiled[i] != original[i])
                {
                    if (diffs < 20)
                        Console.WriteLine($"  Diff at 0x{i:X}: compiled=0x{compiled[i]:X2} original=0x{original[i]:X2}");
                    diffs++;
                }
            }

            if (diffs == 0)
            {
                Console.WriteLine("PASS: byte-identical output");
                return 0;
            }

            Console.WriteLine($"FAIL: {diffs} byte differences");
            return 1;
        });
        return cmd;
    }

    private static Command BuildBatchDecompileCommand()
    {
        var dirArg = new Argument<DirectoryInfo>("directory") { Description = "Directory containing RPJ files" };
        var outputOpt = new Option<DirectoryInfo?>("-o") { Description = "Output directory" };
        var formatOpt = new Option<string>("--format") { Description = "Output format: bdsl (default) or bds", DefaultValueFactory = _ => "bdsl" };
        var cmd = new Command("batch-decompile", "Decompile all RPJ files in directory");
        cmd.Arguments.Add(dirArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(formatOpt);
        cmd.SetAction(parseResult =>
        {
            var dir = parseResult.GetValue(dirArg)!;
            var outputDir = parseResult.GetValue(outputOpt) ?? dir;
            var format = parseResult.GetValue(formatOpt) ?? "bdsl";
            string ext = format == "bds" ? ".bds" : ".bdsl";

            var files = Directory.GetFiles(dir.FullName, "*.rpj", SearchOption.AllDirectories);
            int success = 0, fail = 0;

            foreach (var file in files)
            {
                string rel = Path.GetRelativePath(dir.FullName, file);
                string outPath = Path.Combine(outputDir.FullName, Path.ChangeExtension(rel, ext));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                try
                {
                    var rpj = RPJ.Reader.Read(file);
                    if (format == "bds")
                        BDS.Writer.Write(rpj, outPath);
                    else
                        BDSL.Writer.Write(rpj, outPath);
                    success++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  FAIL: {rel}: {ex.Message}");
                    fail++;
                }
            }

            Console.WriteLine($"{success}/{files.Length} succeeded{(fail > 0 ? $", {fail} failed" : "")}");
            return fail > 0 ? 1 : 0;
        });
        return cmd;
    }

    private static Command BuildBatchCompileCommand()
    {
        var dirArg = new Argument<DirectoryInfo>("directory") { Description = "Directory containing BDSL or BDS files" };
        var outputOpt = new Option<DirectoryInfo?>("-o") { Description = "Output directory" };
        var cmd = new Command("batch-compile", "Compile all BDSL/BDS files in directory");
        cmd.Arguments.Add(dirArg);
        cmd.Options.Add(outputOpt);
        cmd.SetAction(parseResult =>
        {
            var dir = parseResult.GetValue(dirArg)!;
            var outputDir = parseResult.GetValue(outputOpt) ?? dir;

            var bdslFiles = Directory.GetFiles(dir.FullName, "*.bdsl", SearchOption.AllDirectories);
            var bdsFiles = Directory.GetFiles(dir.FullName, "*.bds", SearchOption.AllDirectories);
            var files = bdslFiles.Concat(bdsFiles).ToArray();
            int success = 0, fail = 0;

            foreach (var file in files)
            {
                string rel = Path.GetRelativePath(dir.FullName, file);
                string outPath = Path.Combine(outputDir.FullName, Path.ChangeExtension(rel, ".rpj"));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                try
                {
                    RPJ.SceneFile rpj;
                    if (Path.GetExtension(file).Equals(".bds", StringComparison.OrdinalIgnoreCase))
                        rpj = BDS.Parser.ParseFile(file);
                    else
                        rpj = BDSL.Parser.ParseFile(file);
                    RPJ.Writer.Write(rpj, outPath);
                    success++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  FAIL: {rel}: {ex.Message}");
                    fail++;
                }
            }

            Console.WriteLine($"{success}/{files.Length} succeeded{(fail > 0 ? $", {fail} failed" : "")}");
            return fail > 0 ? 1 : 0;
        });
        return cmd;
    }

    private static Command BuildBatchVerifyCommand()
    {
        var dirArg = new Argument<DirectoryInfo>("directory") { Description = "Directory containing RPJ files" };
        var strictOpt = new Option<bool>("--strict") { Description = "Ignore header string padding differences" };
        var formatOpt = new Option<string>("--format") { Description = "Round-trip format: bdsl (default) or bds", DefaultValueFactory = _ => "bdsl" };
        var cmd = new Command("batch-verify", "Verify round-trip for all RPJ files");
        cmd.Arguments.Add(dirArg);
        cmd.Options.Add(strictOpt);
        cmd.Options.Add(formatOpt);
        cmd.SetAction(parseResult =>
        {
            var dir = parseResult.GetValue(dirArg)!;
            bool strict = parseResult.GetValue(strictOpt);
            var format = parseResult.GetValue(formatOpt) ?? "bdsl";

            var files = Directory.GetFiles(dir.FullName, "*.rpj", SearchOption.AllDirectories);
            int pass = 0, fail = 0;
            var failures = new List<string>();

            foreach (var file in files)
            {
                string rel = Path.GetRelativePath(dir.FullName, file);
                try
                {
                    var original = File.ReadAllBytes(file);
                    var rpj = RPJ.Reader.Read(file);

                    byte[] compiled;
                    if (format == "bds")
                    {
                        var bdsText = BDS.Writer.Write(rpj);
                        var rpjBack = BDS.Parser.Parse(bdsText);
                        compiled = RPJ.Writer.Write(rpjBack);
                    }
                    else
                    {
                        var bdslText = BDSL.Writer.Write(rpj);
                        var rpjBack = BDSL.Parser.Parse(bdslText);
                        compiled = RPJ.Writer.Write(rpjBack);
                    }

                    if (compiled.Length != original.Length)
                    {
                        failures.Add($"{rel}: size mismatch ({compiled.Length} vs {original.Length})");
                        fail++;
                        continue;
                    }

                    int diffs = 0;
                    for (int i = 0; i < compiled.Length; i++)
                    {
                        if (compiled[i] != original[i])
                        {
                            if (strict && IsHeaderStringRegion(i)) continue;
                            diffs++;
                        }
                    }

                    if (diffs == 0) pass++;
                    else { failures.Add($"{rel}: {diffs} byte differences"); fail++; }
                }
                catch (Exception ex)
                {
                    failures.Add($"{rel}: {ex.Message}");
                    fail++;
                }
            }

            Console.WriteLine($"\n{pass} passed, {fail} failed out of {files.Length}");
            foreach (var f in failures)
                Console.WriteLine($"  {f}");
            return fail > 0 ? 1 : 0;
        });
        return cmd;
    }

    private static bool IsHeaderStringRegion(int offset)
    {
        return (offset >= 0x0C && offset <= 0x4B)
            || (offset >= 0x4C && offset <= 0x67)
            || (offset >= 0x68 && offset <= 0x16F)
            || (offset >= 0x170 && offset <= 0x26F);
    }
}
