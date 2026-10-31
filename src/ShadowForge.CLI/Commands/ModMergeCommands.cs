using System.CommandLine;
using Microsoft.Extensions.Logging;
using ShadowForge.GameData.Mods;
using ShadowForge.GameData.Mods.Merge;

namespace ShadowForge.CLI.Commands;

/// <summary>
/// "mod check" reports whether installed mods can run together and what they both change.
/// "mod merge" combines them into one mod when nothing they change overlaps.
/// </summary>
internal static class ModMergeCommands
{
    private const int DefaultConflictsPerFile = 10;

    public static Command BuildCheck(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var modsArg = ModsArgument();
        var rootOpt = new GameRootOption();
        var profileOpt = ModCommands.ProfileOption();
        var allOpt = AllConflictsOption();
        var jsonOpt = CliOutput.CreateJsonOption();
        var cmd = new Command("check",
            "Report which files the mods share, whether they merge, and every item, enemy, row or script line two mods change differently.");
        cmd.Arguments.Add(modsArg);
        cmd.Options.Add(rootOpt);
        cmd.Options.Add(profileOpt);
        cmd.Options.Add(allOpt);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("mod.check");
            bool json = pr.GetValue(jsonOpt);
            try
            {
                var plan = new ModMerger(rootOpt.Locate(pr), pr.GetValue(profileOpt)).Plan(pr.GetValue(modsArg)!);
                if (!json)
                    PrintReport(plan, null, pr.GetValue(allOpt));
                return CliOutput.Success(json, "mod.check", [], Warnings(plan));
            }
            catch (Exception ex)
            {
                log.LogError("{Message}", ex.Message);
                return CliOutput.Failure(json, "mod.check", ex.Message);
            }
        });
        return cmd;
    }

    public static Command BuildMerge(Func<ParseResult, ILoggerFactory> getFactory)
    {
        var modsArg = ModsArgument();
        var nameOpt = new Option<string>("--name") { Description = "Name of the merged mod folder.", Required = true };
        var outOpt = new Option<string?>("--out") { Description = "Folder to create the merged mod in (default: the install's mods\\)." };
        var forceOpt = new Option<bool>("--force") { Description = "Replace an existing mod folder of that name." };
        var enableOpt = new Option<bool>("--enable")
            { Description = "Enable the merged mod and disable the mods it was made from in mod_order.txt." };
        var rootOpt = new GameRootOption();
        var profileOpt = ModCommands.ProfileOption();
        var allOpt = AllConflictsOption();
        var jsonOpt = CliOutput.CreateJsonOption();
        var cmd = new Command("merge",
            "Merge installed mods into one. Nothing is written when two mods change the same thing in different ways.");
        cmd.Arguments.Add(modsArg);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(outOpt);
        cmd.Options.Add(forceOpt);
        cmd.Options.Add(enableOpt);
        cmd.Options.Add(rootOpt);
        cmd.Options.Add(profileOpt);
        cmd.Options.Add(allOpt);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(pr =>
        {
            var log = getFactory(pr).CreateLogger("mod.merge");
            bool json = pr.GetValue(jsonOpt);
            try
            {
                var install = rootOpt.Locate(pr);
                var mods = pr.GetValue(modsArg)!;
                string name = ModPath.Segment(pr.GetValue(nameOpt)!, "the merged mod name");
                if (mods.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"'{name}' is one of the mods being merged. Pick another name.");
                if (pr.GetValue(enableOpt) && pr.GetValue(outOpt) is not null)
                    throw new InvalidOperationException("--enable needs the merged mod in the install's mods folder, so leave out --out.");

                var merger = new ModMerger(install, pr.GetValue(profileOpt));
                string modDir = Path.Combine(pr.GetValue(outOpt) ?? merger.ModsRoot, name);
                if (Directory.Exists(modDir) && !pr.GetValue(forceOpt))
                    throw new InvalidOperationException($"{modDir} already exists. Pass --force to replace it.");
                if (Directory.Exists(modDir) && !File.Exists(Path.Combine(modDir, "mod.toml")))
                    throw new InvalidOperationException($"{modDir} is not a mod folder (no mod.toml), so it is not replaced.");

                var plan = merger.Plan(mods);
                if (!json)
                    PrintReport(plan, name, pr.GetValue(allOpt));
                if (!plan.CanWrite)
                    return CliOutput.Failure(json, "mod.merge",
                        $"{plan.Blocked.Count()} file(s) cannot be merged; nothing was written.", warnings: Warnings(plan));

                if (Directory.Exists(modDir)) Directory.Delete(modDir, recursive: true);
                int written = ModMerger.Write(plan, modDir, name);
                if (!json)
                    Console.WriteLine($"Wrote {written} file(s) to {modDir}");
                if (pr.GetValue(enableOpt))
                {
                    foreach (string source in mods)
                        ModOrderEdit.Apply(install, source, enable: false, quiet: json, profile: pr.GetValue(profileOpt));
                    ModOrderEdit.Apply(install, name, enable: true, quiet: json, profile: pr.GetValue(profileOpt));
                }
                return CliOutput.Success(json, "mod.merge", [modDir], Warnings(plan));
            }
            catch (Exception ex)
            {
                log.LogError("{Message}", ex.Message);
                return CliOutput.Failure(json, "mod.merge", ex.Message);
            }
        });
        return cmd;
    }

    private static Argument<string[]> ModsArgument() => new("mods")
    {
        Description = "Mod folder names under mods\\, two or more.",
        Arity = ArgumentArity.OneOrMore,
    };

    private static Option<bool> AllConflictsOption() =>
        new("--all-conflicts") { Description = $"List every conflict in a file, not just the first {DefaultConflictsPerFile}." };

    private static void PrintReport(MergePlan plan, string? name, bool allConflicts)
    {
        foreach (string line in MergeReport.Lines(plan, name, allConflicts ? int.MaxValue : DefaultConflictsPerFile))
            Console.WriteLine(line);
    }

    /// <summary>
    /// One warning per file that cannot be merged and per file an enabled DLC shadows.
    /// </summary>
    private static List<CliWarning> Warnings(MergePlan plan)
    {
        var warnings = plan.Blocked
            .Select(f => new CliWarning(f.Result.Problem is null ? "conflict" : "unmergeable",
                f.Result.Problem is { } problem
                    ? $"{f.VfsPath}: {problem}"
                    : $"{f.VfsPath}: {string.Join("; ", f.Result.Conflicts.Select(c => c.Location))}"))
            .ToList();
        warnings.AddRange(plan.Shadowed.Select(f =>
            new CliWarning("shadowed", $"{f.VfsPath} is ignored while {string.Join(", ", f.Packages)} is enabled")));
        return warnings;
    }
}
