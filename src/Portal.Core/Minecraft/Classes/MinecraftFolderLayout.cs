namespace Portal.Core.Minecraft.Classes;

public enum MinecraftFolderKind
{
    Auto,
    Standard,
    Modrinth,
    ModrinthInstance,
    MultiMc,
    MultiMcInstance,
    CurseForge,
    CurseForgeInstance,
    PortalMc,
    Unknown
}

public sealed record MinecraftFolderLayout(
    MinecraftFolderKind Kind,
    string SelectedPath,
    string RootPath,
    string DisplayName)
{
    public bool SupportsTraditionalInstallation => Kind == MinecraftFolderKind.Standard;

    public bool SupportsInstallation => Kind == MinecraftFolderKind.PortalMc;

    private static string GetMultiMcBrand(string path)
    {
        var name = Path.GetFileName(Path.GetFullPath(path));
        if (name.Equals(".BakaXL", StringComparison.OrdinalIgnoreCase)) return "BakaXL";
        if (name.Equals("PrismLauncher", StringComparison.OrdinalIgnoreCase)) return "Prism Launcher";
        if (name.Equals("MultiMC", StringComparison.OrdinalIgnoreCase)) return "MultiMC";
        return "MultiMC / Prism Launcher";
    }

    private static string GetMultiMcInstanceBrand(string path)
    {
        var parent = Directory.GetParent(path);
        while (parent != null)
        {
            var brand = GetMultiMcBrand(parent.FullName);
            if (brand != "MultiMC / Prism Launcher")
                return $"{brand} 实例";
            parent = parent.Parent;
        }
        return "MultiMC / Prism Launcher 实例";
    }

    private static bool IsPortalMcRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Directory.Exists(Path.Combine(path, "meta")) &&
            Directory.Exists(Path.Combine(path, "instances")))
            return true;
        return Path.GetFileName(Path.GetFullPath(path))
            .Equals("cc.tiouo.portal.minecraft", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryFindPortalMcRoot(string path, out string root)
    {
        var current = new DirectoryInfo(path);
        while (current != null)
        {
            if (IsPortalMcRoot(current.FullName))
            {
                root = current.FullName;
                return true;
            }
            current = current.Parent;
        }
        root = string.Empty;
        return false;
    }

    public static MinecraftFolderLayout Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new(MinecraftFolderKind.Unknown, string.Empty, string.Empty, "未识别的 Minecraft 文件夹");
        var selected = Path.GetFullPath(path.Trim());

        
        if (Directory.Exists(Path.Combine(selected, "instances")) &&
            Directory.Exists(Path.Combine(selected, "libraries")) &&
            Directory.Exists(Path.Combine(selected, "assets")) &&
            Directory.Exists(Path.Combine(selected, "meta", "net.minecraft")))
            return new(MinecraftFolderKind.MultiMc, selected, selected, GetMultiMcBrand(selected));

        
        if (File.Exists(Path.Combine(selected, "package.info")) &&
            TryFindMultiMcRoot(selected, out var multiMcRoot))
            return new(MinecraftFolderKind.MultiMcInstance, selected, multiMcRoot, GetMultiMcInstanceBrand(selected));

        
        if (IsPortalMcRoot(selected))
            return new(MinecraftFolderKind.PortalMc, selected, selected, "Portal MC");
        if (TryFindPortalMcRoot(selected, out var portalMcRoot))
            return new(MinecraftFolderKind.PortalMc, portalMcRoot, portalMcRoot, "Portal MC");

        
        if (Directory.Exists(Path.Combine(selected, "Install", "versions")) &&
            Directory.Exists(Path.Combine(selected, "Instances")))
            return new(MinecraftFolderKind.CurseForge, selected, selected, "CurseForge");

        if (Path.GetFileName(selected).Equals("Instances", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(Path.Combine(Directory.GetParent(selected)?.FullName ?? string.Empty, "Install", "versions")))
        {
            var root = Directory.GetParent(selected)!.FullName;
            return new(MinecraftFolderKind.CurseForge, selected, root, "CurseForge");
        }

        
        if (File.Exists(Path.Combine(selected, "minecraftinstance.json")) &&
            TryFindParentDirectory(selected, "Install", "Instances", out var curseForgeRoot))
            return new(MinecraftFolderKind.CurseForgeInstance, selected, curseForgeRoot, "CurseForge 实例");

        
        if (Directory.Exists(Path.Combine(selected, "Install")) &&
            Path.GetFileName(selected).Equals("minecraft", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(Directory.GetParent(selected)?.FullName ?? string.Empty)
                .Equals("curseforge", StringComparison.OrdinalIgnoreCase))
            return new(MinecraftFolderKind.CurseForge, selected, selected, "CurseForge");

        
        if (Directory.Exists(Path.Combine(selected, "profiles")) &&
            Directory.Exists(Path.Combine(selected, "meta")) &&
            Directory.Exists(Path.Combine(selected, "caches")))
            return new(MinecraftFolderKind.Modrinth, selected, selected, "Modrinth");

        
        if (File.Exists(Path.Combine(selected, "app.db")) &&
            Directory.Exists(Path.Combine(selected, "profiles")) &&
            Directory.Exists(Path.Combine(selected, "meta")))
            return new(MinecraftFolderKind.Modrinth, selected, selected, "Modrinth");

        
        if (TryFindModrinthRoot(selected, out var modrinthRoot) &&
            IsUnder(selected, Path.Combine(modrinthRoot, "profiles")))
            return new(MinecraftFolderKind.ModrinthInstance, selected, modrinthRoot, "Modrinth 实例");

        
        if (Directory.Exists(Path.Combine(selected, "instances")) &&
            Directory.Exists(Path.Combine(selected, "libraries")) &&
            Directory.Exists(Path.Combine(selected, "assets")))
            return new(MinecraftFolderKind.MultiMc, selected, selected, GetMultiMcBrand(selected));

        
        if (File.Exists(Path.Combine(selected, "instance.cfg")) && File.Exists(Path.Combine(selected, "mmc-pack.json")))
            return new(MinecraftFolderKind.MultiMcInstance, selected,
                Directory.GetParent(Directory.GetParent(selected)?.FullName ?? selected)?.FullName ?? selected,
                GetMultiMcInstanceBrand(selected));

        if (Path.GetFileName(selected).Equals(".minecraft", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(Directory.GetParent(selected)?.FullName ?? string.Empty, "instance.cfg")))
        {
            var instanceRoot = Directory.GetParent(selected)!.FullName;
            return new(MinecraftFolderKind.MultiMcInstance, instanceRoot,
                Directory.GetParent(Directory.GetParent(instanceRoot)?.FullName ?? instanceRoot)?.FullName ?? instanceRoot,
                GetMultiMcInstanceBrand(instanceRoot));
        }

        
        if (Directory.Exists(Path.Combine(selected, "versions")) ||
            Directory.Exists(Path.Combine(selected, "bedrock_versions")) ||
            Path.GetFileName(selected).Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            return new(MinecraftFolderKind.Standard, selected, selected, "传统 .minecraft 文件夹");

        if (Directory.Exists(Path.Combine(selected, ".minecraft")))
            return new(MinecraftFolderKind.Standard, selected, Path.Combine(selected, ".minecraft"),
                "传统 .minecraft 文件夹");

        return new(MinecraftFolderKind.Standard, selected, selected, "传统 .minecraft 文件夹");
    }

    public static MinecraftFolderLayout FromFolderKind(MinecraftFolderKind kind, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new(MinecraftFolderKind.Unknown, string.Empty, string.Empty, "未识别的 Minecraft 文件夹");
        var selected = Path.GetFullPath(path.Trim());
        var displayName = kind switch
        {
            MinecraftFolderKind.Modrinth or MinecraftFolderKind.ModrinthInstance => "Modrinth",
            MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance => GetMultiMcBrand(selected),
            MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance => "CurseForge",
            MinecraftFolderKind.PortalMc => "Portal MC",
            MinecraftFolderKind.Standard => "传统 .minecraft 文件夹",
            _ => "未识别的 Minecraft 文件夹"
        };
        return new(kind, selected, selected, displayName);
    }

    public static string ResolveGameFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var selected = Path.GetFullPath(path.Trim());

        var layout = Detect(selected);
        if (layout.Kind != MinecraftFolderKind.Standard && layout.Kind != MinecraftFolderKind.Unknown)
            return layout.SelectedPath;

        if (Directory.Exists(Path.Combine(selected, "versions")) ||
            Directory.Exists(Path.Combine(selected, "bedrock_versions")) ||
            Path.GetFileName(selected).Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            return selected;

        var nested = Path.Combine(selected, ".minecraft");
        if (Directory.Exists(nested))
            return nested;

        var launcherChildren = Directory.Exists(selected)
            ? Directory.GetDirectories(selected).Where(IsLauncherTypeFolder).ToArray()
            : [];
        return launcherChildren.Length == 1 ? launcherChildren[0] : selected;
    }

    private static bool IsLauncherTypeFolder(string path)
    {
        var kind = Detect(path).Kind;
        return kind is not (MinecraftFolderKind.Standard or MinecraftFolderKind.Unknown);
    }

    public static bool LooksLikeMinecraftRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var selected = Path.GetFullPath(path.Trim());
        return Directory.Exists(Path.Combine(selected, "versions")) ||
               Directory.Exists(Path.Combine(selected, "bedrock_versions")) ||
               Directory.Exists(Path.Combine(selected, ".minecraft"));
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

    private static bool TryFindMultiMcRoot(string path, out string root)
    {
        var current = new DirectoryInfo(path);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "instances")) &&
                Directory.Exists(Path.Combine(current.FullName, "libraries")) &&
                Directory.Exists(Path.Combine(current.FullName, "assets")) &&
                Directory.Exists(Path.Combine(current.FullName, "meta", "net.minecraft")))
            {
                root = current.FullName;
                return true;
            }
            current = current.Parent;
        }
        root = string.Empty;
        return false;
    }

    private static bool TryFindModrinthRoot(string path, out string root)
    {
        var current = new DirectoryInfo(path);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "profiles")) &&
                Directory.Exists(Path.Combine(current.FullName, "meta")) &&
                (File.Exists(Path.Combine(current.FullName, "app.db")) ||
                 Directory.Exists(Path.Combine(current.FullName, "caches"))))
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
        MinecraftFolderKind.Modrinth or MinecraftFolderKind.ModrinthInstance => "Modrinth",
        MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance => "MultiMC / Prism Launcher / BakaXL",
        MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance => "CurseForge",
        MinecraftFolderKind.PortalMc => "Portal MC",
        MinecraftFolderKind.Standard => "传统 .minecraft",
        _ => "未知"
    };
}