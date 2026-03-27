/**
 * @file        Commands/HdbCommands.cs
 * @brief       CLI commands for HDB model operations
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */
using System.CommandLine;
using ShadowForge.Formats.HDB;
using ShadowForge.Formats.GLTF;

namespace ShadowForge.Cli.Commands;

public static class HdbCommands
{
    public static Command Create()
    {
        var cmd = new Command("hdb", "HDB model operations");
        cmd.Subcommands.Add(BuildExportCommand());
        cmd.Subcommands.Add(BuildBatchExportCommand());
        return cmd;
    }

    private static Command BuildExportCommand()
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "HDB model file" };
        var outputOpt = new Option<FileInfo?>("-o") { Description = "Output .glb file" };
        var cmd = new Command("export", "Export HDB model to glTF (.glb)");
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(outputOpt);
        cmd.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var output = parseResult.GetValue(outputOpt)
                ?? new FileInfo(Path.ChangeExtension(file.FullName, ".glb"));

            Console.WriteLine($"Reading {file.Name}...");
            var model = Reader.Read(file.FullName);
            Console.WriteLine($"  {model.Bones.Count} bones, {model.VertexArrays.Count} vertex arrays, {model.MeshGroups.Count} mesh groups");

            Console.WriteLine($"Exporting to {output.Name}...");
            Exporter.Export(model, output.FullName);
            Console.WriteLine($"Done. {new FileInfo(output.FullName).Length:N0} bytes written.");
        });
        return cmd;
    }

    private static Command BuildBatchExportCommand()
    {
        var dirArg = new Argument<DirectoryInfo>("directory") { Description = "Directory containing HDB files" };
        var outputOpt = new Option<DirectoryInfo?>("-o") { Description = "Output directory" };
        var cmd = new Command("batch-export", "Export all HDB files in a directory to glTF");
        cmd.Arguments.Add(dirArg);
        cmd.Options.Add(outputOpt);
        cmd.SetAction(parseResult =>
        {
            var dir = parseResult.GetValue(dirArg)!;
            var outputDir = parseResult.GetValue(outputOpt) ?? dir;
            if (!outputDir.Exists) outputDir.Create();

            var files = dir.GetFiles("*.hdb", SearchOption.AllDirectories);
            Console.WriteLine($"Found {files.Length} HDB files.");

            int ok = 0, fail = 0;
            foreach (var file in files)
            {
                try
                {
                    var model = Reader.Read(file.FullName);
                    var outPath = Path.Combine(outputDir.FullName,
                        Path.ChangeExtension(file.Name, ".glb"));
                    Exporter.Export(model, outPath);
                    Console.WriteLine($"  OK: {file.Name}");
                    ok++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  FAIL: {file.Name} - {ex.Message}");
                    fail++;
                }
            }

            Console.WriteLine($"Done. {ok} exported, {fail} failed.");
        });
        return cmd;
    }
}
