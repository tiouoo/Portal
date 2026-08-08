namespace Portal.Core.Minecraft.Modpack;

public sealed record ModpackExportOption
{
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string? Rules { get; init; }
    public string? ShowRules { get; init; }
    public bool DefaultChecked { get; init; } = true;
    public bool RequireModLoader { get; init; }
    public bool RequireOptiFine { get; init; }
    public bool RequireModLoaderOrOptiFine { get; init; }
}
public static class ModpackExportRuleBuilder
{
    public static readonly IReadOnlyList<string> BuiltInExcludes =
    [
        "!*.log", "!*.dat_old", "!*.BakaCoreInfo", "!hmclversion.cfg", "!log4j2.xml"
    ];
    
    public static IEnumerable<string> BuildRules(IEnumerable<ModpackExportOption> options)
    {
        foreach (var option in options)
        {
            if (string.IsNullOrEmpty(option.Rules))
                continue;

            var lines = option.Rules.Split('|');
            if (lines.Length == 0)
                continue;

            yield return "// " + option.Title;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    yield return trimmed;
            }
        }

        foreach (var exclude in BuiltInExcludes)
            yield return exclude;
    }

    public static IEnumerable<string> StandardizeLines(IEnumerable<string> raw, bool addFolderGlob)
    {
        foreach (var lineRaw in raw)
        {
            var line = lineRaw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//") || line.StartsWith("="))
                continue;

            line = line.Replace('/', '\\');
            yield return line + (addFolderGlob && line.EndsWith('\\') ? "**" : "");
        }
    }

    public static bool IsOptionVisible(string gameRoot, ModpackExportOption option,
        bool hasOptiFine, bool modded)
    {
        if (option.RequireOptiFine && !hasOptiFine) return false;
        if (option.RequireModLoader && !modded) return false;
        if (option.RequireModLoaderOrOptiFine && !hasOptiFine && !modded) return false;

        var rules = option.Rules ?? option.ShowRules;
        if (string.IsNullOrEmpty(rules))
            return true;

        var allEntries = EnumerateVisibleEntries(gameRoot);
        if (allEntries == null)
            return true;

        var standardized = StandardizeLines(rules.Split('|'), true).ToList();
        return allEntries.Value.AllEntries.Any(entry => standardized.Any(rule =>
            !rule.StartsWith('!') && ModpackGlobMatcher.Like(entry, rule)));
    }

    private static (List<string> AllEntries, List<DirectoryInfo> SubFolders)? EnumerateVisibleEntries(
        string gameRoot)
    {
        var root = new DirectoryInfo(gameRoot);
        if (!root.Exists)
            return null;

        try
        {
            var allEntries = new List<string>();
            var subFolders = new List<DirectoryInfo>();
            allEntries.AddRange(root.EnumerateFiles().Select(f => f.Name));
            foreach (var subFolder in root.EnumerateDirectories())
            {
                if (!IsValidDirectory(subFolder)) continue;
                subFolders.Add(subFolder);
                allEntries.Add($"{subFolder.Name}/");
                allEntries.AddRange(subFolder.EnumerateFiles().Select(f => $"{subFolder.Name}/{f.Name}"));
                allEntries.AddRange(subFolder.EnumerateDirectories().Where(IsValidDirectory)
                    .Select(d => $"{subFolder.Name}/{d.Name}/"));
            }

            return (allEntries, subFolders);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidDirectory(DirectoryInfo folder)
    {
        try
        {
            return folder.Exists && folder.EnumerateFileSystemInfos().Any();
        }
        catch
        {
            return false;
        }
    }
}