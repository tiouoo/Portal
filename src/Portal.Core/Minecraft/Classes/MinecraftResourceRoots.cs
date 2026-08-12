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
            // Portal MC 与 Modrinth 的共享资源统一放在 meta 目录下
            case MinecraftFolderKind.PortalMc:
            case MinecraftFolderKind.Modrinth:
            case MinecraftFolderKind.ModrinthInstance:
                yield return Path.Combine(root, "meta");
                break;

            // CurseForge 的共享资源放在 Install 目录下（还有 Install/versions）
            case MinecraftFolderKind.CurseForge:
            case MinecraftFolderKind.CurseForgeInstance:
                yield return Path.Combine(root, "Install");
                break;

            // 传统 .minecraft 与 MultiMC / Prism / BakaXL 的 libraries、assets 直接位于根目录
            case MinecraftFolderKind.MultiMc:
            case MinecraftFolderKind.MultiMcInstance:
            case MinecraftFolderKind.Standard:
                yield return root;
                break;
        }
    }
}