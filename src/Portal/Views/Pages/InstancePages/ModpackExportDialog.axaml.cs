using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using LoaderType = Iridium.Enums.LoaderType;
using Portal.Core.Minecraft.Modpack;
using Portal.Localization;
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
            CanResize = false
        };

        return await OverlayDialog.ShowCustomAsync<ModpackExportOptions>(dialog, dialog.DataContext, hostId,
            options);
    }
}

public class ExportOptionItem : ObservableObject
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
    private static readonly string[] SubOptionBlackList = ["Quark Programmer Art.zip", "+ EuphoriaPatches_"];
    private readonly MinecraftInstance _instance;

    public ModpackExportDialogViewModel(MinecraftInstance instance)
    {
        _instance = instance;
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
        BuildOptions();
        IncludePortalIcon = CanExportPortalIcon;
    }

    [ObservableProperty] public partial string PackName { get; set; } = string.Empty;
    [ObservableProperty] public partial string PackVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial bool BundleAllFiles { get; set; }
    [ObservableProperty] public partial bool ModrinthOnly { get; set; }
    [ObservableProperty] public partial bool IncludePortalSettings { get; set; }
    [ObservableProperty] public partial bool IncludePortalIcon { get; set; }

    public string DefaultPackName => _instance.InstanceName;
    public string PackSummary => _instance.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(PackSummary);
    public string InstanceName => _instance.InstanceName;
    public bool CanUseModrinthMode => !BundleAllFiles;

    public bool CanExportPortalIcon => _instance.GetExportIconPath() != null;

    public ObservableCollection<ExportOptionItem> Options { get; } = [];

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }

    private bool HasOptiFine =>
        _instance.MinecraftEntry is { Loaders: { Count: > 0 } loaders } &&
        loaders.Any(loader => loader.Type == LoaderType.Optifine);

    private bool IsModded
    {
        get
        {
            if (_instance.MinecraftEntry is { Loaders.Count: > 0 })
                return true;
            return !_instance.IsVanilla;
        }
    }

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    partial void OnBundleAllFilesChanged(bool value)
    {
        if (value)
            ModrinthOnly = false;
        OnPropertyChanged(nameof(CanUseModrinthMode));
    }

    private void BuildOptions()
    {
        var gameRoot = _instance.GetJavaGameDirectory();
        Options.Clear();

        bool Visible(ExportOptionItem option)
        {
            return ModpackExportRuleBuilder.IsOptionVisible(gameRoot, new ModpackExportOption
            {
                Rules = option.Rules,
                ShowRules = option.ShowRules,
                RequireModLoader = option.RequireModLoader,
                RequireOptiFine = option.RequireOptiFine,
                RequireModLoaderOrOptiFine = option.RequireModLoaderOrOptiFine
            }, HasOptiFine, IsModded);
        }

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
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(ExportOptionItem.IsChecked)) return;
                    if (!item.IsChecked) return;
                    foreach (var child in children)
                        child.IsChecked = true;
                };

            if (!Visible(item))
                return;
            Options.Add(item);
            if (children is { Count: > 0 })
                foreach (var child in children)
                {
                    child.Parent = item;
                    if (Visible(child))
                        Options.Add(child);
                }
        }


        Add(CommonLanguageManager.Instance.modpackExport_game.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_gameDescription.CurrentValue(), null);


        Add(CommonLanguageManager.Instance.modpackExport_settings.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_settingsDescription.CurrentValue(),
            "options.txt|configureddefaults/");
        Add(CommonLanguageManager.Instance.modpackExport_personalInfo.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_personalInfoDescription.CurrentValue(),
            "hotbar.nbt|command_history.txt", defaultChecked: false);
        Add(CommonLanguageManager.Instance.modpackExport_optifineSettings.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_optifineSettingsDescription.CurrentValue(),
            "optionsof.txt|optionsshaders.txt",
            requireOptiFine: true);


        Add(CommonLanguageManager.Instance.modpackExport_modFiles.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_modFilesDescription.CurrentValue(),
            "mods/|!mods/*.disabled|!mods/*.old|!mods/.connector/|coremods/|lib/|!mods/mcef-libraries/|!mods/mcef-cache/",
            requireModLoader: true);
        Add(CommonLanguageManager.Instance.modpackExport_disabledMods.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_disabledModsDescription.CurrentValue(),
            "mods/*.disabled|mods/*.old",
            defaultChecked: false, requireModLoader: true, indent: 1);


        Add(CommonLanguageManager.Instance.modpackExport_packData.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_packDataDescription.CurrentValue(),
            "addons/|multiblocked/|modpack-update-checker/|global_packs/|global_resource_packs/|global_data_packs/|optional_data_packs/|maps/|icon.png|mods-resourcepacks/|matmos/|resource_assorts/|resource_assorts.json|patchouli_books/|datapacks/|kubejs*/|!kubejs*/probe/|!kubejs*/exported/|!kubejs*/jsconfig.json|!kubejs*/README.txt|openloader/|worldshape/|resources/|scripts/|structures/|fontfiles/|oresources/|packmenu/|craftpresence/|pointblanks/|template*/|!template*/playerdata/|!template*/stats/");
        Add(CommonLanguageManager.Instance.modpackExport_modSettings.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_modSettingsDescription.CurrentValue(),
            "config/|!config/accountsx/|!config/jei/world/|!config/worldedit/|config/worldedit/worldedit.properties|!config/spark/|config/spark/config.json|defaultconfigs/|journeymap/config/|journeymap/server/|TrashSlotSaveState.json|customfov.txt|gg.essential.mod/|essential/|!essential/*/|!essential/*.jar*|!essential/screenshot-checksum-caches.json|!essential/microsoft_accounts.json|paragliderSettings.nbt|local/client_config.json|local/ftbl.json|local/client/sidebar_buttons.json|local/client/ftbutilities.cfg|local/client/ftblib.cfg|local/client/xencraft.cfg|liteloader.properties|default_reference.xml|CustomSkinLoader/CustomSkinLoader.json");


        Add(CommonLanguageManager.Instance.modpackExport_mapsWaypoints.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_mapsWaypointsDescription.CurrentValue(),
            "journeymap/data/|xaero/|XaeroWaypoints/|XaeroWorldMap/",
            defaultChecked: false);
        Add(CommonLanguageManager.Instance.modpackExport_jeiBookmarks.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_jeiBookmarksDescription.CurrentValue(),
            "config/jei/world/", defaultChecked: false);
        Add(CommonLanguageManager.Instance.modpackExport_emiBookmarks.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_emiBookmarksDescription.CurrentValue(),
            "emi.json", defaultChecked: false);
        Add(CommonLanguageManager.Instance.modpackExport_patchouliData.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_patchouliDataDescription.CurrentValue(),
            "patchouli_data.json", defaultChecked: false);


        var resourcePacks = BuildSubOptions(gameRoot, ["resourcepacks", "texturepacks"], true);
        if (resourcePacks.Count > 0)
            Add(CommonLanguageManager.Instance.modpackExport_resourcePacks.CurrentValue(),
                CommonLanguageManager.Instance.modpackExport_resourcePacksDescription.CurrentValue(), null,
                "resourcepacks/|texturepacks/",
                children: resourcePacks);

        var shaderPacks = BuildSubOptions(gameRoot, ["shaderpacks"], true);
        if (shaderPacks.Count > 0)
            Add(CommonLanguageManager.Instance.modpackExport_shaderPacks.CurrentValue(),
                CommonLanguageManager.Instance.modpackExport_shaderPacksDescription.CurrentValue(), null,
                "shaderpacks/",
                requireModLoaderOrOptiFine: true, children: shaderPacks);

        Add(CommonLanguageManager.Instance.modpackExport_screenshots.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_screenshotsDescription.CurrentValue(),
            "screenshots/", defaultChecked: false);
        Add(CommonLanguageManager.Instance.modpackExport_schematics.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_schematicsDescription.CurrentValue(),
            "schematics/", defaultChecked: false);
        Add(CommonLanguageManager.Instance.modpackExport_recordings.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_recordingsDescription.CurrentValue(),
            "replay_recordings/|replay_videos/", defaultChecked: false,
            requireModLoader: true);


        var saves = BuildSubOptions(gameRoot, ["saves"], false);
        if (saves.Count > 0)
            Add(CommonLanguageManager.Instance.modpackExport_saves.CurrentValue(),
                CommonLanguageManager.Instance.modpackExport_savesDescription.CurrentValue(), null, "saves/", false,
                children: saves);

        Add(CommonLanguageManager.Instance.modpackExport_serverList.CurrentValue(),
            CommonLanguageManager.Instance.modpackExport_serverListDescription.CurrentValue(),
            "servers.dat", defaultChecked: false);


        var others = BuildOtherFolders(gameRoot);
        if (others.Count > 0)
            Add(CommonLanguageManager.Instance.modpackExport_otherFolders.CurrentValue(),
                CommonLanguageManager.Instance.modpackExport_otherFoldersDescription.CurrentValue(), null,
                defaultChecked: false, children: others);
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
            IncludePortalSettings = IncludePortalSettings,
            IncludePortalIcon = IncludePortalIcon
        };

        RequestClose?.Invoke(this, options);
    }

    private static ModpackExportOption ToOption(ExportOptionItem item)
    {
        return new ModpackExportOption
        {
            Title = item.Title,
            Rules = item.Rules,
            DefaultChecked = item.IsChecked
        };
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}