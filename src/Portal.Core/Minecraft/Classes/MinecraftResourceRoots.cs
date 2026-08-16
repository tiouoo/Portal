namespace Portal.Core.Minecraft.Classes;

public static class MinecraftResourceRoots
{
    public static IEnumerable<string> Resolve(MinecraftFolderEntry folder)
    {
        if (string.IsNullOrWhiteSpace(folder?.FolderPath) || !Directory.Exists(folder.FolderPath))
            yield break;

        var layout = folder.DetectedLayout;
        var root = layout.RootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        switch (layout.Kind)
        {
            case MinecraftFolderKind.PortalMc:
            case MinecraftFolderKind.Modrinth:
            case MinecraftFolderKind.ModrinthInstance:
                yield return Path.Combine(root, "meta");
                break;


            case MinecraftFolderKind.CurseForge:
            case MinecraftFolderKind.CurseForgeInstance:
                yield return Path.Combine(root, "Install");
                break;


            case MinecraftFolderKind.MultiMc:
            case MinecraftFolderKind.MultiMcInstance:
            case MinecraftFolderKind.Standard:
                yield return root;
                break;
        }
    }

    public static IReadOnlyList<string> ResolveForInstall(IEnumerable<MinecraftFolderEntry> folders,
        string? targetFolderPath)
    {
        if (folders is null)
            return [];

        try
        {
            return folders
                .Where(folder => !string.IsNullOrWhiteSpace(folder?.FolderPath))
                .Where(folder => string.IsNullOrWhiteSpace(targetFolderPath)
                                 || !string.Equals(folder.FolderPath, targetFolderPath,
                                     StringComparison.OrdinalIgnoreCase))
                .SelectMany(Resolve)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}