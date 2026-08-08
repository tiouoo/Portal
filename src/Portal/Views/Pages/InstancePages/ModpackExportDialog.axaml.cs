using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Modpack;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

internal partial class ModpackExportDialog : UserControl
{
    public ModpackExportDialog(MinecraftInstance instance)
    {
        InitializeComponent();
        DataContext = new ModpackExportDialogViewModel(instance);
    }
}

public static class ModpackExportDialogHost
{
    public static async Task<ModpackExportOptions?> Show(MinecraftInstance instance, string? hostId)
    {
        var dialog = new ModpackExportDialog(instance);
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
        };

        return await OverlayDialog.ShowCustomAsync<ModpackExportOptions>(dialog, dialog.DataContext, hostId: hostId,
            options: options);
    }
}

public partial class ExportOptionItem : ObservableObject
{
    private bool _isChecked;

    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string? Rules { get; init; }
    public string? ShowRules { get; init; }
    public bool RequireModLoader { get; init; }
    public bool RequireOptiFine { get; init; }
    public bool RequireModLoaderOrOptiFine { get; init; }
    public int Indent { get; init; }
    public bool IsEnabled { get; init; } = true;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
                OnPropertyChanged(nameof(HasDescription));
        }
    }

    public IReadOnlyList<ExportOptionItem>? Children { get; init; }
    public ExportOptionItem? Parent { get; set; }
    public bool HasChildren => Children is { Count: > 0 };
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public Thickness IndentMargin => new(Indent * 24, 0, 0, 0);
}

public partial class ModpackExportDialogViewModel : ObservableObject, IDialogContext
{
    private readonly MinecraftInstance _instance;

    [ObservableProperty] public partial string PackName { get; set; } = string.Empty;
    [ObservableProperty] public partial string PackVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial bool BundleAllFiles { get; set; }
    [ObservableProperty] public partial bool ModrinthOnly { get; set; }
    [ObservableProperty] public partial bool IncludePortalSettings { get; set; }

    public string DefaultPackName => _instance.InstanceName;
    public string PackSummary => _instance.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(PackSummary);
    public string InstanceName => _instance.InstanceName;
    public bool CanUseModrinthMode => !BundleAllFiles;

    partial void OnBundleAllFilesChanged(bool value)
    {
        if (value)
            ModrinthOnly = false;
        OnPropertyChanged(nameof(CanUseModrinthMode));
    }

    public ObservableCollection<ExportOptionItem> Options { get; } = [];

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }

    private static readonly string[] SubOptionBlackList = ["Quark Programmer Art.zip", "+ EuphoriaPatches_"];

    public ModpackExportDialogViewModel(MinecraftInstance instance)
    {
        _instance = instance;
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
        BuildOptions();
    }

    private bool HasOptiFine =>
        _instance.MinecraftEntry is ModifiedMinecraftEntry modified &&
        modified.ModLoaders.Any(loader => loader.Type == ModLoaderType.OptiFine);

    private bool IsModded
    {
        get
        {
            if (_instance.MinecraftEntry is ModifiedMinecraftEntry modified)
                return modified.ModLoaders.Any();
            return !_instance.IsVanilla;
        }
    }

    private void BuildOptions()
    {
        var gameRoot = _instance.GetJavaGameDirectory();
        Options.Clear();

        bool Visible(ExportOptionItem option) =>
            ModpackExportRuleBuilder.IsOptionVisible(gameRoot, new ModpackExportOption
            {
                Rules = option.Rules,
                ShowRules = option.ShowRules,
                RequireModLoader = option.RequireModLoader,
                RequireOptiFine = option.RequireOptiFine,
                RequireModLoaderOrOptiFine = option.RequireModLoaderOrOptiFine
            }, HasOptiFine, IsModded);

        void Add(string title, string? description, string? rules, string? showRules = null,
            bool defaultChecked = true, bool requireModLoader = false, bool requireOptiFine = false,
            bool requireModLoaderOrOptiFine = false, bool enabled = true, int indent = 0,
            IReadOnlyList<ExportOptionItem>? children = null)
        {
            var item = new ExportOptionItem
            {
                Title = title,
                Description = description,
                Rules = rules,
                ShowRules = showRules,
                RequireModLoader = requireModLoader,
                RequireOptiFine = requireOptiFine,
                RequireModLoaderOrOptiFine = requireModLoaderOrOptiFine,
                Indent = indent,
                IsEnabled = enabled,
                IsChecked = defaultChecked,
                Children = children
            };
            if (children is { Count: > 0 })
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(ExportOptionItem.IsChecked)) return;
                    if (!item.IsChecked) return;
                    foreach (var child in children)
                        child.IsChecked = true;
                };
            }

            if (!Visible(item))
                return;
            Options.Add(item);
            if (children is { Count: > 0 })
            {
                foreach (var child in children)
                {
                    child.Parent = item;
                    if (Visible(child))
                        Options.Add(child);
                }
            }
        }

        // 游戏本体（不产生规则，仅用于展示）
        Add("游戏本体", "包含 Minecraft 本体与启动必要文件", null);

        // 游戏设置
        Add("游戏设置", "包含游戏画面、控制等设置（options.txt 等）", "options.txt|configureddefaults/");
        Add("个人信息", "包含玩家的快捷栏与指令历史", "hotbar.nbt|command_history.txt", defaultChecked: false);
        Add("OptiFine 设置", "包含 OptiFine 的画面与光影设置", "optionsof.txt|optionsshaders.txt",
            requireOptiFine: true);

        // 模组
        Add("模组文件", "包含模组及其核心、库文件", "mods/|!mods/*.disabled|!mods/*.old|!mods/.connector/|coremods/|lib/|!mods/mcef-libraries/|!mods/mcef-cache/",
            requireModLoader: true);
        Add("被禁用的模组", "包含被停用的模组文件（*.disabled）", "mods/*.disabled|mods/*.old",
            defaultChecked: false, requireModLoader: true, indent: 1);

        // 整合包重要数据
        Add("整合包重要数据", "包含整合包的数据文件、数据包与自定义内容",
            "addons/|multiblocked/|modpack-update-checker/|global_packs/|global_resource_packs/|global_data_packs/|optional_data_packs/|maps/|icon.png|mods-resourcepacks/|matmos/|resource_assorts/|resource_assorts.json|patchouli_books/|datapacks/|kubejs*/|!kubejs*/probe/|!kubejs*/exported/|!kubejs*/jsconfig.json|!kubejs*/README.txt|openloader/|worldshape/|resources/|scripts/|structures/|fontfiles/|oresources/|packmenu/|craftpresence/|pointblanks/|template*/|!template*/playerdata/|!template*/stats/");
        Add("模组设置", "包含模组的配置文件与默认配置",
            "config/|!config/accountsx/|!config/jei/world/|!config/worldedit/|config/worldedit/worldedit.properties|!config/spark/|config/spark/config.json|defaultconfigs/|journeymap/config/|journeymap/server/|TrashSlotSaveState.json|customfov.txt|gg.essential.mod/|essential/|!essential/*/|!essential/*.jar*|!essential/screenshot-checksum-caches.json|!essential/microsoft_accounts.json|paragliderSettings.nbt|local/client_config.json|local/ftbl.json|local/client/sidebar_buttons.json|local/client/ftbutilities.cfg|local/client/ftblib.cfg|local/client/xencraft.cfg|liteloader.properties|default_reference.xml|CustomSkinLoader/CustomSkinLoader.json");

        // 地图 / 书签
        Add("地图与航点", "包含小地图数据与航点", "journeymap/data/|xaero/|XaeroWaypoints/|XaeroWorldMap/",
            defaultChecked: false);
        Add("JEI 书签", "包含 JEI 物品管理器保存的书签", "config/jei/world/", defaultChecked: false);
        Add("EMI 书签", "包含 EMI 物品管理器保存的书签", "emi.json", defaultChecked: false);
        Add("帕秋莉手册数据", "包含帕秋莉手册的阅读进度", "patchouli_data.json", defaultChecked: false);

        // 资源包 / 光影包（动态子项）
        var resourcePacks = BuildSubOptions(gameRoot, ["resourcepacks", "texturepacks"], defaultChecked: true);
        if (resourcePacks.Count > 0)
        {
            Add("资源包", "包含选中的资源包与纹理包", null, showRules: "resourcepacks/|texturepacks/",
                children: resourcePacks);
        }

        var shaderPacks = BuildSubOptions(gameRoot, ["shaderpacks"], defaultChecked: true);
        if (shaderPacks.Count > 0)
        {
            Add("光影包", "包含选中的光影包及其配置文件", null, showRules: "shaderpacks/",
                requireModLoaderOrOptiFine: true, children: shaderPacks);
        }

        Add("截图", "包含游戏内截图（screenshots 文件夹）", "screenshots/", defaultChecked: false);
        Add("建筑蓝图", "包含小木斧等导出的建筑蓝图（schematics）", "schematics/", defaultChecked: false);
        Add("录像", "包含回放模组的录像文件", "replay_recordings/|replay_videos/", defaultChecked: false,
            requireModLoader: true);

        // 存档（动态子项）
        var saves = BuildSubOptions(gameRoot, ["saves"], defaultChecked: false);
        if (saves.Count > 0)
        {
            Add("存档", "包含选中的世界存档", null, showRules: "saves/", defaultChecked: false, children: saves);
        }

        Add("服务器列表", "包含已保存的服务器列表（servers.dat）", "servers.dat", defaultChecked: false);

        // 其他文件夹
        var others = BuildOtherFolders(gameRoot);
        if (others.Count > 0)
        {
            Add("其他文件夹", "包含实例目录中未归类的大容量文件夹", null, defaultChecked: false, children: others);
        }
    }

    private static List<ExportOptionItem> BuildSubOptions(string gameRoot, IReadOnlyList<string> folders,
        bool defaultChecked)
    {
        var items = new List<ExportOptionItem>();
        foreach (var folder in folders)
        {
            var targetFolder = new DirectoryInfo(Path.Combine(gameRoot, folder));
            if (!targetFolder.Exists)
                continue;

            foreach (var file in targetFolder.EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly)
                         .Concat(targetFolder.EnumerateFiles("*.rar", SearchOption.TopDirectoryOnly)))
            {
                if (SubOptionBlackList.Any(b => file.Name.Contains(b, StringComparison.OrdinalIgnoreCase)))
                    continue;
                items.Add(new ExportOptionItem
                {
                    Title = file.Name,
                    Rules = $"{folder}/{file.Name}",
                    Indent = 1,
                    IsChecked = defaultChecked
                });
            }

            foreach (var subFolder in targetFolder.EnumerateDirectories().OrderByDescending(f => f.LastWriteTime))
            {
                if (SubOptionBlackList.Any(b => subFolder.Name.Contains(b, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!SafeHasContent(subFolder))
                    continue;
                items.Add(new ExportOptionItem
                {
                    Title = subFolder.Name,
                    Rules = $"{folder}/{subFolder.Name}/",
                    Indent = 1,
                    IsChecked = defaultChecked
                });
            }
        }

        return items;
    }

    private List<ExportOptionItem> BuildOtherFolders(string gameRoot)
    {
        var coveredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mods", "coremods", "lib",
            "addons", "multiblocked", "modpack-update-checker", "global_packs",
            "global_resource_packs", "global_data_packs", "optional_data_packs", "maps",
            "mods-resourcepacks", "matmos", "resource_assorts",
            "patchouli_books", "datapacks",
            "openloader", "worldshape", "resources", "scripts", "structures",
            "fontfiles", "oresources", "packmenu", "craftpresence", "pointblanks",
            "config", "defaultconfigs", "journeymap", "local", "essential", "gg.essential.mod",
            "CustomSkinLoader",
            "xaero", "XaeroWaypoints", "XaeroWorldMap",
            "resourcepacks", "texturepacks",
            "shaderpacks",
            "screenshots", "schematics",
            "replay_recordings", "replay_videos",
            "saves", "configureddefaults",
            "assets", "versions", "libraries", "structureCacheV1",
            ".fabric", ".git", "avatar-cache", "cosmetic-cache",
            "Portal"
        };

        var coveredPrefixes = new[] { "kubejs", "template" };
        var coveredSuffixes = new[] { "-natives" };

        var root = new DirectoryInfo(gameRoot);
        if (!root.Exists)
            return [];

        var items = new List<ExportOptionItem>();
        foreach (var subDir in root.EnumerateDirectories())
        {
            if (coveredFolders.Contains(subDir.Name))
                continue;
            if (coveredPrefixes.Any(p => subDir.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (coveredSuffixes.Any(s => subDir.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!SafeHasContent(subDir))
                continue;

            items.Add(new ExportOptionItem
            {
                Title = subDir.Name,
                Rules = $"{subDir.Name}/",
                Indent = 1,
                IsChecked = false
            });
        }

        return items;
    }

    private static bool SafeHasContent(DirectoryInfo folder)
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

    private void Confirm()
    {
        var checkedOptions = Options.Where(o => o.IsChecked && (o.Parent is null || o.Parent.IsChecked)).ToList();
        var rawRules = ModpackExportRuleBuilder.BuildRules(checkedOptions.Select(ToOption));
        var rules = ModpackExportRuleBuilder.StandardizeLines(rawRules, true).ToList();

        var options = new ModpackExportOptions
        {
            PackName = string.IsNullOrWhiteSpace(PackName) ? DefaultPackName : PackName.Trim(),
            PackVersion = string.IsNullOrWhiteSpace(PackVersion) ? "1.0.0" : PackVersion.Trim(),
            PackSummary = PackSummary,
            Rules = rules,
            CheckHostedAssets = !BundleAllFiles,
            ModrinthOnly = ModrinthOnly,
            IncludePortalSettings = IncludePortalSettings
        };

        RequestClose?.Invoke(this, options);
    }

    private static ModpackExportOption ToOption(ExportOptionItem item) => new()
    {
        Title = item.Title,
        Rules = item.Rules,
        DefaultChecked = item.IsChecked
    };

    private void Cancel() => RequestClose?.Invoke(this, null);

    public void Close() => Cancel();

    public event EventHandler<object?>? RequestClose;
}
