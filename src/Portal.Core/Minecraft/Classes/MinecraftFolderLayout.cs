namespace Portal.Core.Minecraft.Classes;

public enum MinecraftFolderKind
{
    Auto,
    Standard,
    ModrinthApp,
    ModrinthProfile,
    MultiMc,
    MultiMcInstance,
    BakaXl,
    BakaXlInstance,
    CurseForge,
    CurseForgeInstance,
    Unknown
}

public sealed record MinecraftFolderLayout(
    MinecraftFolderKind Kind,
    string SelectedPath,
    string RootPath,
    string DisplayName)
{
    public bool SupportsTraditionalInstallation => Kind == MinecraftFolderKind.Standard;

    public static MinecraftFolderLayout Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new(MinecraftFolderKind.Unknown, string.Empty, string.Empty, "未识别的 Minecraft 文件夹");
        var selected = Path.GetFullPath(path.Trim());

        if (Directory.Exists(Path.Combine(selected, "instances")) &&
            Directory.Exists(Path.Combine(selected, "libraries")) &&
            Directory.Exists(Path.Combine(selected, "assets")) &&
            Directory.Exists(Path.Combine(selected, "meta", "net.minecraft")) &&
            Path.GetFileName(selected).Equals("minecraft", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(Directory.GetParent(selected)?.FullName ?? string.Empty)
                .Equals(".BakaXL", StringComparison.OrdinalIgnoreCase))
            return new(MinecraftFolderKind.BakaXl, selected, selected, "BakaXL");

        if (File.Exists(Path.Combine(selected, "package.info")) &&
            TryFindBakaXlRoot(selected, out var bakaXlRoot))
            return new(MinecraftFolderKind.BakaXlInstance, selected, bakaXlRoot, "BakaXL 实例");

        if (Directory.Exists(Path.Combine(selected, "Install", "versions")) &&
            Directory.Exists(Path.Combine(selected, "Instances")))
            return new(MinecraftFolderKind.CurseForge, selected, selected, "CurseForge App");

        if (Path.GetFileName(selected).Equals("Instances", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(Path.Combine(Directory.GetParent(selected)?.FullName ?? string.Empty, "Install", "versions")))
        {
            var root = Directory.GetParent(selected)!.FullName;
            return new(MinecraftFolderKind.CurseForge, selected, root, "CurseForge App");
        }

        if (File.Exists(Path.Combine(selected, "minecraftinstance.json")) &&
            TryFindParentDirectory(selected, "Install", "Instances", out var curseForgeRoot))
            return new(MinecraftFolderKind.CurseForgeInstance, selected, curseForgeRoot, "CurseForge App 实例");

        if (File.Exists(Path.Combine(selected, "app.db")) &&
            Directory.Exists(Path.Combine(selected, "profiles")) &&
            Directory.Exists(Path.Combine(selected, "meta")))
            return new(MinecraftFolderKind.ModrinthApp, selected, selected, "Modrinth App");

        if (TryFindParent(selected, "app.db", "meta", out var modrinthRoot) &&
            IsUnder(selected, Path.Combine(modrinthRoot, "profiles")))
            return new(MinecraftFolderKind.ModrinthProfile, selected, modrinthRoot, "Modrinth App 实例");

        if (Directory.Exists(Path.Combine(selected, "instances")) &&
            Directory.Exists(Path.Combine(selected, "libraries")) &&
            Directory.Exists(Path.Combine(selected, "assets")))
            return new(MinecraftFolderKind.MultiMc, selected, selected, "MultiMC / Prism Launcher");

        if (File.Exists(Path.Combine(selected, "instance.cfg")) && File.Exists(Path.Combine(selected, "mmc-pack.json")))
            return new(MinecraftFolderKind.MultiMcInstance, selected,
                Directory.GetParent(Directory.GetParent(selected)?.FullName ?? selected)?.FullName ?? selected,
                "MultiMC / Prism Launcher 实例");

        if (Path.GetFileName(selected).Equals(".minecraft", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(Directory.GetParent(selected)?.FullName ?? string.Empty, "instance.cfg")))
        {
            var instanceRoot = Directory.GetParent(selected)!.FullName;
            return new(MinecraftFolderKind.MultiMcInstance, instanceRoot,
                Directory.GetParent(Directory.GetParent(instanceRoot)?.FullName ?? instanceRoot)?.FullName ?? instanceRoot,
                "MultiMC / Prism Launcher 实例");
        }

        if (Directory.Exists(Path.Combine(selected, "versions")) ||
            Directory.Exists(Path.Combine(selected, "bedrock_versions")) ||
            Path.GetFileName(selected).Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            return new(MinecraftFolderKind.Standard, selected, selected, "传统 .minecraft 文件夹");

        if (Directory.Exists(Path.Combine(selected, ".minecraft")))
            return new(MinecraftFolderKind.Standard, selected, Path.Combine(selected, ".minecraft"),
                "传统 .minecraft 文件夹");

        // Retain the legacy behavior for manually added roots. A valid game root may be empty,
        // contain only Bedrock versions, or receive its versions after it is configured.
        return new(MinecraftFolderKind.Standard, selected, selected, "传统 .minecraft 文件夹");
    }

    private static bool TryFindParent(string path, string markerFile, string markerDirectory, out string root)
    {
        var current = new DirectoryInfo(path);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, markerFile)) &&
                Directory.Exists(Path.Combine(current.FullName, markerDirectory)))
            {
                root = current.FullName;
                return true;
            }
            current = current.Parent;
        }
        root = string.Empty;
        return false;
    }

    private static bool IsUnder(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool TryFindParentDirectory(string path, string markerDirectory, string childDirectory,
        out string root)
    {
        var current = new DirectoryInfo(path);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, markerDirectory)) &&
                Directory.Exists(Path.Combine(current.FullName, childDirectory)))
            {
                root = current.FullName;
                return true;
            }
            current = current.Parent;
        }
        root = string.Empty;
        return false;
    }

    private static bool TryFindBakaXlRoot(string path, out string root)
    {
        var current = new DirectoryInfo(path);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "instances")) &&
                Directory.Exists(Path.Combine(current.FullName, "meta", "net.minecraft")) &&
                Path.GetFileName(current.FullName).Equals("minecraft", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(current.Parent?.FullName ?? string.Empty)
                    .Equals(".BakaXL", StringComparison.OrdinalIgnoreCase))
            {
                root = current.FullName;
                return true;
            }
            current = current.Parent;
        }
        root = string.Empty;
        return false;
    }
}

public sealed record MinecraftInstanceLayout(
    MinecraftFolderKind Kind,
    string SourceRoot,
    string InstanceRoot,
    string GameDirectory,
    string MetadataRoot,
    string AssetsDirectory,
    string LibrariesDirectory,
    string NativesDirectory,
    string? NativeIconPath = null)
{
    public string KindDisplayName => Kind switch
    {
        MinecraftFolderKind.ModrinthApp or MinecraftFolderKind.ModrinthProfile => "Modrinth App",
        MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance => "MultiMC / Prism Launcher",
        MinecraftFolderKind.BakaXl or MinecraftFolderKind.BakaXlInstance => "BakaXL",
        MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance => "CurseForge App",
        MinecraftFolderKind.Standard => "传统 .minecraft",
        _ => "未知"
    };

}
