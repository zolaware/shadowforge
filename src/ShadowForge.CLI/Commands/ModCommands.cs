using System.CommandLine;
using Microsoft.Extensions.Logging;
using ShadowForge.GameData;
using ShadowForge.GameData.Mods;

namespace ShadowForge.CLI.Commands;

public static class ModCommands
{
    public static Command Create(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var cmd = new Command("mod", "Deploy, build, list, enable, disable, check, and merge mods.");
        cmd.Subcommands.Add(BuildDeploy(getFactory));
        cmd.Subcommands.Add(BuildBuild(getFactory));
        cmd.Subcommands.Add(BuildTrace(getFactory));
        cmd.Subcommands.Add(BuildList(getFactory));
        cmd.Subcommands.Add(BuildEnable(getFactory, enable: true));
        cmd.Subcommands.Add(BuildEnable(getFactory, enable: false));
        cmd.Subcommands.Add(ModMergeCommands.BuildCheck(getFactory));
        cmd.Subcommands.Add(ModMergeCommands.BuildMerge(getFactory));
        return cmd;
    }

    internal static Option<string?> ProfileOption() =>
        new("--profile") { Description = "Profile whose mod_order.txt to target (default: 'default')." };

    private static Command BuildDeploy(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var srcArg = new Argument<string>("source-dir") { Description = "Mod folder to copy into mods\\." };
        var nameOpt = new Option<string?>("--name") { Description = "Destination mod name (default: source folder name)." };
        var rootOpt = new GameRootOption();
        var enableOpt = new Option<bool>("--enable") { Description = "Add the mod to mod_order.txt after the copy." };
        var jsonOpt = CliOutput.CreateJsonOption();
        var cmd = new Command("deploy", "Copy an existing mod folder into the install's mods\\ directory.");
        cmd.Arguments.Add(srcArg);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(rootOpt);
        cmd.Options.Add(enableOpt);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("mod.deploy");
            bool json = pr.GetValue(jsonOpt);
            string src = pr.GetValue(srcArg)!;
            try
            {
                if (!Directory.Exists(src))
                    throw new DirectoryNotFoundException($"Source directory not found: {src}");
                var install = rootOpt.Locate(pr);
                string name = pr.GetValue(nameOpt) is { Length: > 0 } n ? n : new DirectoryInfo(src).Name;
                string destDir = Path.Combine(new ModDeployer(install).ModsRoot, name);
                int copied = CopyTree(src, destDir);
                if (!json)
                    Console.WriteLine($"Copied {copied} file(s) into {destDir}");
                if (pr.GetValue(enableOpt))
                    ModOrderEdit.Apply(install, name, enable: true, quiet: json);
                return CliOutput.Success(json, "mod.deploy", [destDir]);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "deploy failed");
                return CliOutput.Failure(json, "mod.deploy", ex.Message);
            }
        });
        return cmd;
    }

    private static Command BuildBuild(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var srcArg = new Argument<string>("source-dir") { Description = "Mod source tree holding sde.toml and entities/." };
        var installOpt = new Option<string>("--install") { Description = "Install root holding mods\\.", Required = true };
        var rootOpt = new GameRootOption();
        var jsonOpt = CliOutput.CreateJsonOption();
        var cmd = new Command("build", "Assemble a mod from a declared source tree.");
        cmd.Arguments.Add(srcArg);
        cmd.Options.Add(installOpt);
        cmd.Options.Add(rootOpt);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("mod.build");
            bool json = pr.GetValue(jsonOpt);
            try
            {
                string installRoot = pr.GetValue(installOpt)!;
                var install = GameInstall.Locate(pr.GetValue(rootOpt) ?? Path.Combine(installRoot, "game"));
                var source = ModSource.Load(pr.GetValue(srcArg)!);
                var result = new ModBuilder(install).Build(source, Path.Combine(installRoot, "mods"));
                log.LogInformation("built {Name}: {Count} file(s), {Entities} entity(s)",
                    source.Name, result.FilesWritten, result.Entities.Count);
                return CliOutput.Success(json, "mod.build", [result.ModDir]);
            }
            catch (Exception ex)
            {
                log.LogError("{Message}", ex.Message);
                return CliOutput.Failure(json, "mod.build", ex.Message);
            }
        });
        return cmd;
    }

    private static Command BuildTrace(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var logOpt = new Option<string>("--log") { Description = "Path to logs\\file_access_summary.csv.", Required = true };
        var modOpt = new Option<string>("--mod-dir") { Description = "Built mod folder whose files to check.", Required = true };
        var jsonOpt = CliOutput.CreateJsonOption();
        var cmd = new Command("trace", "Report which of a built mod's files the game requested.");
        cmd.Options.Add(logOpt);
        cmd.Options.Add(modOpt);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("mod.trace");
            bool json = pr.GetValue(jsonOpt);
            try
            {
                var rows = AccessLog.Load(pr.GetValue(logOpt)!);
                string modDir = pr.GetValue(modOpt)!;
                var lines = new List<string>();
                foreach (string file in Directory
                             .EnumerateFiles(modDir, "*", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string gamePath = Path.GetRelativePath(modDir, file).Replace('/', '\\');
                    if (gamePath.Equals("mod.toml", StringComparison.OrdinalIgnoreCase)) continue;
                    var hit = AccessLog.Find(rows, gamePath);
                    string verdict = hit is null ? "never-requested"
                        : hit.Overrides > 0 ? "overridden"
                        : hit.Misses > 0 ? "missed" : "hit";
                    lines.Add($"{verdict}\t{gamePath}");
                    log.LogInformation("{Verdict} {GamePath}", verdict, gamePath);
                }
                return CliOutput.Success(json, "mod.trace", lines);
            }
            catch (Exception ex)
            {
                log.LogError("{Message}", ex.Message);
                return CliOutput.Failure(json, "mod.trace", ex.Message);
            }
        });
        return cmd;
    }

    private static Command BuildList(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var rootOpt = new GameRootOption();
        var profileOpt = ProfileOption();
        var cmd = new Command("list", "List installed mods and whether each is enabled.");
        cmd.Options.Add(rootOpt);
        cmd.Options.Add(profileOpt);
        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("mod.list");
            try
            {
                foreach (var m in new ModDeployer(rootOpt.Locate(pr)).List(pr.GetValue(profileOpt)))
                    Console.WriteLine($"[{(m.Enabled ? "x" : " ")}] {m.Name}");
                return 0;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "list failed");
                return 1;
            }
        });
        return cmd;
    }

    private static Command BuildEnable(Func<ParseResult, ILoggerFactory> getFactory, bool enable)
    {
        string verb = enable ? "enable" : "disable";
        var nameArg = new Argument<string>("name") { Description = "Mod folder name." };
        var rootOpt = new GameRootOption();
        var orderOpt = new Option<string?>("--mod-order")
            { Description = "Explicit mod_order.txt path, overriding profile and root resolution." };
        var profileOpt = ProfileOption();
        var cmd = new Command(verb,
            enable ? "Add a mod to mod_order.txt, where the last mod wins." : "Remove a mod from mod_order.txt.");
        cmd.Arguments.Add(nameArg);
        cmd.Options.Add(rootOpt);
        cmd.Options.Add(orderOpt);
        cmd.Options.Add(profileOpt);
        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("mod." + verb);
            try
            {
                ModOrderEdit.Apply(rootOpt.Locate(pr), pr.GetValue(nameArg)!, enable, quiet: false,
                    pr.GetValue(orderOpt), pr.GetValue(profileOpt));
                return 0;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "{Verb} failed", verb);
                return 1;
            }
        });
        return cmd;
    }

    private static int CopyTree(string srcDir, string destDir)
    {
        int count = 0;
        foreach (string src in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(destDir, Path.GetRelativePath(srcDir, src));
            OutputPath.CreateParentDirectory(dest);
            File.Copy(src, dest, overwrite: true);
            count++;
        }
        return count;
    }
}
