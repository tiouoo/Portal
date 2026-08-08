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
    AxolotlApp,
    AxolotlProfile,
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

    private static bool IsPortalMcRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        // 目录已初始化时以结构为准，允许用户重命名根文件夹。
        if (Directory.Exists(Path.Combine(path, "meta")) &&
            Directory.Exists(Path.Combine(path, "instances")))
            return true;
        // 尚未安装任何版本的空根目录（如默认文件夹刚创建时）按文件夹名识别。
        return Path.GetFileName(Path.GetFullPath(path))
            .Equals("cc.tiouo.portal.minecraft", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从任意子路径向上查找 Portal MC 根目录（含 meta 与 instances 的结构根）。</summary>
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
            Directory.Exists(Path.Combine(selected, "meta", "net.minecraft")) &&
            Path.GetFileName(selected).Equals("minecraft", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(Directory.GetParent(selected)?.FullName ?? string.Empty)
                .Equals(".BakaXL", StringComparison.OrdinalIgnoreCase))
            return new(MinecraftFolderKind.BakaXl, selected, selected, "BakaXL");

        if (File.Exists(Path.Combine(selected, "package.info")) &&
            TryFindBakaXlRoot(selected, out var bakaXlRoot))
            return new(MinecraftFolderKind.BakaXlInstance, selected, bakaXlRoot, "BakaXL 实例");

        // Portal MC 布局：根目录含 meta/versions 与 instances；实例、meta 等子目录选中时向上定位到根。
        if (IsPortalMcRoot(selected))
            return new(MinecraftFolderKind.PortalMc, selected, selected, "Portal MC");
        if (TryFindPortalMcRoot(selected, out var portalMcRoot))
            return new(MinecraftFolderKind.PortalMc, portalMcRoot, portalMcRoot, "Portal MC");

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

        // 全新安装的 CurseForge App 可能尚未创建 Install/versions 或 Instances，
        // 仅凭 UserProfile\curseforge\minecraft 目录结构即可识别。
        if (Directory.Exists(Path.Combine(selected, "Install")) &&
            Path.GetFileName(selected).Equals("minecraft", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(Directory.GetParent(selected)?.FullName ?? string.Empty)
                .Equals("curseforge", StringComparison.OrdinalIgnoreCase))
            return new(MinecraftFolderKind.CurseForge, selected, selected, "CurseForge App");

        // Axolotl（Theseus）结构与 Modrinth 相同，靠应用根目录名区分。
        if (IsAxolotlRoot(selected) &&
            File.Exists(Path.Combine(selected, "app.db")) &&
            Directory.Exists(Path.Combine(selected, "profiles")) &&
            Directory.Exists(Path.Combine(selected, "meta")))
            return new(MinecraftFolderKind.AxolotlApp, selected, selected, "Axolotl");

        if (TryFindParent(selected, "app.db", "meta", out var axolotlRoot) &&
            IsAxolotlRoot(axolotlRoot) &&
            IsUnder(selected, Path.Combine(axolotlRoot, "profiles")))
            return new(MinecraftFolderKind.AxolotlProfile, selected, axolotlRoot, "Axolotl 实例");

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

    /// <summary>
    /// 由已保存的文件夹类型重建布局，用于对应启动器尚未初始化目标目录
    /// （例如空目录）时，无法通过目录结构识别类型的场景。
    /// </summary>
    public static MinecraftFolderLayout FromFolderKind(MinecraftFolderKind kind, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new(MinecraftFolderKind.Unknown, string.Empty, string.Empty, "未识别的 Minecraft 文件夹");
        var selected = Path.GetFullPath(path.Trim());
        var displayName = kind switch
        {
            MinecraftFolderKind.ModrinthApp or MinecraftFolderKind.ModrinthProfile => "Modrinth App",
            MinecraftFolderKind.AxolotlApp or MinecraftFolderKind.AxolotlProfile => "Axolotl",
            MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance => "MultiMC / Prism Launcher",
            MinecraftFolderKind.BakaXl or MinecraftFolderKind.BakaXlInstance => "BakaXL",
            MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance => "CurseForge App",
            MinecraftFolderKind.PortalMc => "Portal MC",
            MinecraftFolderKind.Standard => "传统 .minecraft 文件夹",
            _ => "未识别的 Minecraft 文件夹"
        };
        return new(kind, selected, selected, displayName);
    }

    /// <summary>
    /// 解析用户通过文件选择器选择的文件夹，尝试定位到真正可用的 Minecraft 文件夹。
    /// 适用于用户一时选错、选到外层目录的情况。以下情况不跳转：
    ///  1. 所选文件夹本身已包含 versions / bedrock_versions（即为传统游戏根目录）；
    ///  2. 所选文件夹本身就叫“.minecraft”；
    ///  3. 所选文件夹已被识别为第三方启动器布局（Modrinth / Axolotl / MultiMC / CurseForge / BakaXL 等），
    ///     此时直接采用其识别出的路径。
    /// </summary>
    public static string ResolveGameFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var selected = Path.GetFullPath(path.Trim());

        // 已被识别为其它启动器布局（Modrinth / Axolotl / MultiMC / CurseForge / BakaXL 等），
        // 直接采用其识别出的 SelectedPath（例如 MultiMC 实例根目录）。
        var layout = Detect(selected);
        if (layout.Kind != MinecraftFolderKind.Standard && layout.Kind != MinecraftFolderKind.Unknown)
            return layout.SelectedPath;

        // 传统布局：自身已包含 versions / bedrock_versions，或本身就叫“.minecraft”，视为游戏根目录。
        if (Directory.Exists(Path.Combine(selected, "versions")) ||
            Directory.Exists(Path.Combine(selected, "bedrock_versions")) ||
            Path.GetFileName(selected).Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            return selected;

        // 所选文件夹下层嵌套了实际使用的 .minecraft 文件夹，默认跳转进去。
        var nested = Path.Combine(selected, ".minecraft");
        if (Directory.Exists(nested))
            return nested;

        // 其它启动器类型：若所选文件夹的直接子目录中恰好只有一个可识别的启动器文件夹，则跳转进去。
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

    private static bool IsAxolotlRoot(string path) =>
        Path.GetFileName(Path.GetFullPath(path)).Equals("red.ghs.axolotl", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 判断所选文件夹的下一级是否包含 Minecraft 游戏根目录的特征
    /// （versions / bedrock_versions 子目录，或嵌套的 .minecraft 文件夹）。
    /// 用于提示用户可能选择了错误的文件夹，或该文件夹结构本身存在问题。
    /// </summary>
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
        MinecraftFolderKind.AxolotlApp or MinecraftFolderKind.AxolotlProfile => "Axolotl",
        MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance => "MultiMC / Prism Launcher",
        MinecraftFolderKind.BakaXl or MinecraftFolderKind.BakaXlInstance => "BakaXL",
        MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance => "CurseForge App",
        MinecraftFolderKind.PortalMc => "Portal MC",
        MinecraftFolderKind.Standard => "传统 .minecraft",
        _ => "未知"
    };

}
