namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// The plain-text account of a merge plan: what merges, and for every unit two mods both change,
/// what it is and what each mod does to it. The CLI prints it and a written merge keeps it as
/// the merged mod's readme.
/// </summary>
public static class MergeReport
{
    public static List<string> Lines(MergePlan plan, string? mergedName, int conflictsPerFile)
    {
        var lines = new List<string> { mergedName ?? "Mod compatibility check", "", "Mods, in the order given:" };
        lines.AddRange(plan.Mods.Select((m, i) => $"    {i + 1}. {m}"));
        lines.Add("");
        lines.Add($"{plan.OwnFiles.Count} file(s) shipped by one mod only, copied as they are.");
        lines.Add($"{plan.SharedFiles.Count} file(s) shipped by more than one mod.");

        var merged = plan.SharedFiles.Where(f => f.Result.IsMerged).ToList();
        if (merged.Count > 0)
        {
            lines.Add("");
            lines.Add($"Merged ({merged.Count})");
            foreach (var file in merged)
                lines.Add($"    {Title(file)}  [{string.Join(", ", file.Mods)}]  {file.Result.Method}");
        }

        var blocked = plan.Blocked.ToList();
        if (blocked.Count > 0)
        {
            lines.Add("");
            lines.Add($"Cannot be merged ({blocked.Count})");
            lines.Add("    These mods change the same things in different ways. Nothing is merged");
            lines.Add("    until one of them is changed or left out.");
            foreach (var file in blocked)
                AddBlockedFile(lines, file, conflictsPerFile);
        }

        if (plan.Shadowed.Count > 0)
        {
            lines.Add("");
            lines.Add($"Ignored while the DLC is on ({plan.Shadowed.Count})");
            lines.Add("    An enabled DLC package ships these files, and the game reads its copy");
            lines.Add("    instead of any mod's. Merging does not change that.");
            foreach (var file in plan.Shadowed)
                lines.Add($"    {file.VfsPath}  [{string.Join(", ", file.Mods)}]  shadowed by {string.Join(", ", file.Packages)}");
        }

        if (plan.PatchedDlc.Count > 0)
        {
            lines.Add("");
            lines.Add($"DLC packages edited in place ({plan.PatchedDlc.Count})");
            foreach (var package in plan.PatchedDlc)
                lines.Add($"    {package.Package}: {string.Join(", ", package.ChangedEntries)}  ({package.ArchivePath})");
        }

        lines.Add("");
        lines.Add(plan.CanWrite
            ? $"Result: these mods can be merged. Turn {string.Join(", ", plan.Mods)} off before turning the merged mod on."
            : $"Result: cannot merge. {blocked.Count} file(s) are changed in conflicting ways.");
        return lines;
    }

    private static void AddBlockedFile(List<string> lines, SharedFile file, int conflictsPerFile)
    {
        lines.Add("");
        lines.Add($"    {Title(file)}  [{string.Join(", ", file.Mods)}]");
        if (file.Result.Problem is { } problem)
        {
            lines.Add($"        cannot be compared {file.Result.Method}: {problem}");
            return;
        }

        int width = file.Result.Conflicts.SelectMany(c => c.Sides).Select(s => s.Mod.Length).DefaultIfEmpty(5).Max();
        foreach (var conflict in file.Result.Conflicts.Take(conflictsPerFile))
        {
            lines.Add($"        {conflict.Location}");
            if (conflict.Stock.Length > 0)
                lines.Add($"            {"stock".PadRight(width)}  {conflict.Stock}");
            foreach (var side in conflict.Sides)
                lines.Add($"            {side.Mod.PadRight(width)}  {side.Value}");
        }
        int hidden = file.Result.Conflicts.Count - conflictsPerFile;
        if (hidden > 0)
            lines.Add($"        ...and {hidden} more");
    }

    private static string Title(SharedFile file) =>
        file.Subject is null ? file.VfsPath : $"{file.VfsPath} ({file.Subject})";
}
