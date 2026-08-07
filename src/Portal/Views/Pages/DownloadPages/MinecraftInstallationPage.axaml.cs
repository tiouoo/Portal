using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Installer;
using Portal.Const;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Operations.Java;
using Portal.Services;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

internal partial class MinecraftInstallationPage : UserControl
{
    private readonly MinecraftInstallationViewModel _viewModel;

    public MinecraftInstallationPage(VersionManifestEntry entry)
    {
        InitializeComponent();
        DataContext = _viewModel = new MinecraftInstallationViewModel(entry);
    }

    private void Install_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = _viewModel.InstallAsync();
        _viewModel.Complete();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => _viewModel.Cancel();

    private async void SelectLoaderVersion_OnClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.SelectLoaderVersionAsync(this);
}

public sealed record MinecraftInstallationDialogResult;

public partial class MinecraftInstallationViewModel : ObservableObject, INotifyDataErrorInfo, IDialogContext
{
    // 按 (游戏版本, 加载器) 缓存网络查询结果；限制容量，避免浏览大量版本后常驻内存。
    private static readonly LruCache<(string Version, LoaderKind Kind), IReadOnlyList<IInstallEntry>> LoaderVersionCache = new(128);
    private readonly VersionManifestEntry _vanilla;
    private readonly Dictionary<LoaderKind, IInstallEntry> _selectedLoaders = [];
    private readonly Dictionary<LoaderKind, IReadOnlyList<IInstallEntry>> _availableLoaderVersions = [];
    private readonly Dictionary<LoaderKind, int> _loadGenerations = [];
    private readonly Dictionary<string, List<string>> _errors = [];
    private bool _updatingSelection;
    private int _loadingCount;

    public ObservableCollection<MinecraftFolderEntry> MinecraftFolders { get; } = [];

    public string VanillaVersion => _vanilla.Id;
    [ObservableProperty] public partial MinecraftFolderEntry? SelectedMinecraftFolder { get; set; }
    [ObservableProperty] public partial string CustomVersionId { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsInstalling { get; set; }
    [ObservableProperty] public partial bool IsFabricSelected { get; set; }
    [ObservableProperty] public partial bool IsForgeSelected { get; set; }
    [ObservableProperty] public partial bool IsNeoForgeSelected { get; set; }
    [ObservableProperty] public partial bool IsQuiltSelected { get; set; }
    [ObservableProperty] public partial bool IsOptiFineSelected { get; set; }
    [ObservableProperty] public partial string FabricStatus { get; set; } = "不安装";
    [ObservableProperty] public partial string ForgeStatus { get; set; } = "不安装";
    [ObservableProperty] public partial string NeoForgeStatus { get; set; } = "不安装";
    [ObservableProperty] public partial string QuiltStatus { get; set; } = "不安装";
    [ObservableProperty] public partial string OptiFineStatus { get; set; } = "不安装";

    public bool HasModLoader => IsFabricSelected || IsForgeSelected || IsNeoForgeSelected || IsQuiltSelected ||
                                IsOptiFineSelected;
    public bool CanCustomizeVersionId => true;
    public bool RequiresJava => IsForgeSelected || IsNeoForgeSelected || IsOptiFineSelected;
    public bool CanInstall => !IsInstalling && _loadingCount == 0 && SelectedMinecraftFolder is not null &&
                              IsVersionIdValid() && SelectedLoadersAreReady();
    public bool HasMissingRequiredJavaRuntime => RequiresJava && GetJavaPath() is null;
    public bool CanSelectLoaderVersion => HasModLoader && _loadingCount == 0 && SelectedLoadersAreReady();

    public MinecraftInstallationViewModel(VersionManifestEntry vanilla)
    {
        _vanilla = vanilla;
        foreach (var folder in Data.ConfigEntry.MinecraftFolders.Where(x => x.SupportsInstallation))
            MinecraftFolders.Add(folder);
        SelectedMinecraftFolder = Data.ConfigEntry.DefaultMinecraftFolder ?? MinecraftFolders.FirstOrDefault();
        CustomVersionId = vanilla.Id;
    }

    partial void OnSelectedMinecraftFolderChanged(MinecraftFolderEntry? value) => UpdateVersionState();
    partial void OnCustomVersionIdChanged(string value) => UpdateVersionState();
    partial void OnIsInstallingChanged(bool value) => OnPropertyChanged(nameof(CanInstall));
    partial void OnIsFabricSelectedChanged(bool value) => SelectionChanged(LoaderKind.Fabric, value);
    partial void OnIsForgeSelectedChanged(bool value) => SelectionChanged(LoaderKind.Forge, value);
    partial void OnIsNeoForgeSelectedChanged(bool value) => SelectionChanged(LoaderKind.NeoForge, value);
    partial void OnIsQuiltSelectedChanged(bool value) => SelectionChanged(LoaderKind.Quilt, value);
    partial void OnIsOptiFineSelectedChanged(bool value) => SelectionChanged(LoaderKind.OptiFine, value);

    private void SelectionChanged(LoaderKind kind, bool selected)
    {
        if (_updatingSelection) return;

        _updatingSelection = true;
        try
        {
            if (selected)
            {
                if (kind == LoaderKind.OptiFine)
                {
                    IsFabricSelected = false;
                    IsNeoForgeSelected = false;
                    IsQuiltSelected = false;
                }
                else
                {
                    IsFabricSelected = kind == LoaderKind.Fabric;
                    IsForgeSelected = kind == LoaderKind.Forge;
                    IsNeoForgeSelected = kind == LoaderKind.NeoForge;
                    IsQuiltSelected = kind == LoaderKind.Quilt;
                    if (kind != LoaderKind.Forge) IsOptiFineSelected = false;
                }
            }

            foreach (var loaderKind in Enum.GetValues<LoaderKind>())
            {
                if (!IsSelected(loaderKind))
                {
                    _selectedLoaders.Remove(loaderKind);
                    _availableLoaderVersions.Remove(loaderKind);
                    SetStatus(loaderKind, "不安装");
                    _loadGenerations[loaderKind] = _loadGenerations.GetValueOrDefault(loaderKind) + 1;
                }
            }
        }
        finally
        {
            _updatingSelection = false;
        }

        CustomVersionId = CreateRecommendedVersionId();
        UpdateVersionState();
        if (selected && IsSelected(kind)) _ = LoadLatestAsync(kind);
    }

    private async Task LoadLatestAsync(LoaderKind kind)
    {
        var generation = _loadGenerations.GetValueOrDefault(kind) + 1;
        _loadGenerations[kind] = generation;
        _loadingCount++;
        SetStatus(kind, "正在获取最新版...");
        OnPropertyChanged(nameof(CanInstall));
        try
        {
            if (!LoaderVersionCache.TryGetValue((_vanilla.Id, kind), out var entries))
            {
                entries = await FetchVersionsAsync(kind);
                LoaderVersionCache.Set((_vanilla.Id, kind), entries);
            }

            if (!IsSelected(kind) || _loadGenerations.GetValueOrDefault(kind) != generation) return;
            if (entries.Count == 0)
            {
                _selectedLoaders.Remove(kind);
                _availableLoaderVersions.Remove(kind);
                SetStatus(kind, "当前游戏版本不可用");
            }
            else
            {
                var entry = entries[0];
                _availableLoaderVersions[kind] = entries;
                _selectedLoaders[kind] = entry;
                SetStatus(kind, $"最新版：{GetLoaderVersion(kind, entry)}");
                CustomVersionId = CreateRecommendedVersionId();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (IsSelected(kind) && _loadGenerations.GetValueOrDefault(kind) == generation)
            {
                _selectedLoaders.Remove(kind);
                _availableLoaderVersions.Remove(kind);
                SetStatus(kind, "获取失败，请取消后重试");
            }
        }
        finally
        {
            _loadingCount--;
            UpdateVersionState();
        }
    }

    private async Task<IReadOnlyList<IInstallEntry>> FetchVersionsAsync(LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => (await FabricInstaller.EnumerableFabricAsync(_vanilla.Id)).Cast<IInstallEntry>().ToList(),
        LoaderKind.Forge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla.Id)).Cast<IInstallEntry>().ToList(),
        LoaderKind.NeoForge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla.Id, true)).Cast<IInstallEntry>().ToList(),
        LoaderKind.Quilt => (await QuiltInstaller.EnumerableQuiltAsync(_vanilla.Id)).Cast<IInstallEntry>().ToList(),
        LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(_vanilla.Id)).Cast<IInstallEntry>().ToList(),
        _ => []
    };

    public async Task SelectLoaderVersionAsync(Control owner)
    {
        if (!CanSelectLoaderVersion) return;

        var versions = _availableLoaderVersions
            .Where(pair => IsSelected(pair.Key))
            .SelectMany(pair => pair.Value.Select(entry => new LoaderVersionItem(pair.Key, entry, GetLoaderVersion(pair.Key, entry))))
            .ToList();
        var selected = await OverlayDialog.ShowCustomAsync<LoaderVersionDialog, LoaderVersionDialogViewModel,
            LoaderVersionItem>(new LoaderVersionDialogViewModel(versions), owner.GetTopLevel().TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = "选择加载器版本", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false, VerticalAnchor = VerticalPosition.Top, VerticalOffset = 80
            });
        if (selected is null || !IsSelected(selected.Kind)) return;

        _selectedLoaders[selected.Kind] = selected.Entry;
        SetStatus(selected.Kind, $"已选择：{selected.Version}");
        CustomVersionId = CreateRecommendedVersionId();
        UpdateVersionState();
    }

    public async Task InstallAsync()
    {
        if (!CanInstall || SelectedMinecraftFolder is null) return;
        var versionId = EffectiveVersionId();
        var folder = SelectedMinecraftFolder;
        var selectedEntries = _selectedLoaders.ToDictionary(x => x.Key, x => x.Value);
        var javaPath = GetJavaPath();
        IsInstalling = true;
        var task = CreateInstallationTask(_vanilla, folder, versionId, selectedEntries, javaPath);
        task.Start();
        try
        {
            await task.Completion;
        }
        finally
        {
            IsInstalling = false;
            UpdateVersionState();
        }
    }

    /// <summary>
    /// 创建一次 Minecraft（可含加载器）安装任务；loaders 传空字典即纯原版安装。供本页与命令行/portal:// 调用共用。
    /// </summary>
    public static ManagedTask CreateInstallationTask(VersionManifestEntry vanilla, MinecraftFolderEntry folder,
        string versionId, IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath) =>
        TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装 Minecraft Java {versionId}",
            Description = "正在创建安装任务",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消安装",
                    Description = "取消当前安装任务。",
                    IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) =>
                    {
                        managedTask.RequestCancellation();
                        return Task.CompletedTask;
                    },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, context => RunInstallationAsync(context, vanilla, folder, versionId, selectedEntries, javaPath));

    internal static bool RequiresJavaRuntime(IEnumerable<LoaderKind> kinds) =>
        kinds.Any(kind => kind is LoaderKind.Forge or LoaderKind.NeoForge or LoaderKind.OptiFine);

    private static async Task RunInstallationAsync(TaskExecutionContext context, VersionManifestEntry vanilla,
        MinecraftFolderEntry folder, string versionId,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        if (folder.DetectedLayout.Kind == MinecraftFolderKind.PortalMc)
        {
            await RunPortalMcInstallationAsync(context, vanilla, folder, versionId, selectedEntries, javaPath);
            return;
        }

        await RunTraditionalInstallationAsync(context, vanilla, folder, versionId, selectedEntries, javaPath);
    }

    /// <summary>
    /// Portal MC 布局安装：原版版本与共享资源写入 meta，加载器版本与实例 json 写入 instances，
    /// 纯原版实例仅写入继承 meta 原版版本的极简 json。
    /// </summary>
    private static async Task RunPortalMcInstallationAsync(TaskExecutionContext context, VersionManifestEntry vanilla,
        MinecraftFolderEntry folder, string versionId,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        var metaRoot = Path.Combine(folder.FolderPath, "meta");
        var instancesRoot = Path.Combine(folder.FolderPath, "instances");
        var hasLoaders = selectedEntries.Count > 0;

        await RunStepAsync(context, "验证安装配置", "正在检查安装目录、实例 ID 和 Java 运行时", async step =>
        {
            if (RequiresJavaRuntime(selectedEntries.Keys) && string.IsNullOrWhiteSpace(javaPath))
            {
                var runtime = await JavaAutoInstallCoordinator.EnsureAsync(GetRecommendedJavaVersion(vanilla.Id),
                    progress => ReportJavaInstallProgress(step, progress), step.CancellationToken);
                javaPath = runtime?.JavaPath;
                if (string.IsNullOrWhiteSpace(javaPath))
                    throw new InvalidOperationException("所选安装方案需要有效的 Java 运行时。");
            }
            if (Directory.Exists(Path.Combine(instancesRoot, versionId)))
                throw new InvalidOperationException($"实例 ID “{versionId}”已存在于所选文件夹，请更换名称。");
            step.ReportProgress(1);
            await Task.CompletedTask;
        });

        var instanceDirectory = Path.Combine(instancesRoot, versionId);
        // Mod-loader profiles must always inherit from the canonical vanilla ID.
        var vanillaId = hasLoaders ? vanilla.Id : versionId;
        var vanillaDirectory = Path.Combine(metaRoot, "versions", vanillaId);
        var vanillaDirectoryExisted = Directory.Exists(vanillaDirectory);
        // 加载器 id 与原版 id 相同时无法直接写入 meta/versions（会命中已存在的原版 json），
        // 使用临时 id 安装，安装完成后再整体移入 instances。
        var effectiveLoaderId = hasLoaders && versionId.Equals(vanilla.Id, StringComparison.OrdinalIgnoreCase)
            ? $"{versionId}.portal-tmp"
            : versionId;
        try
        {
            var primary = selectedEntries.FirstOrDefault(x => x.Key != LoaderKind.OptiFine);
            var primaryEntry = primary.Value;
            var primaryInstaller = primaryEntry is null
                ? null
                : CreatePrimaryInstaller(primary.Key, primaryEntry, metaRoot, effectiveLoaderId, javaPath);
            var optifineInstaller = selectedEntries.TryGetValue(LoaderKind.OptiFine, out var optifineEntry)
                ? CreatePreloadOptifineInstaller(metaRoot, (OptifineInstallEntry)optifineEntry, javaPath)
                : null;

            var vanillaTask = RunStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {vanilla.Id}", async step =>
            {
                var installer = VanillaInstaller.Create(metaRoot, vanilla, vanillaId);
                AttachProgressReporter(installer, step);
                return await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
            });
            var preloadTasks = new List<Task>();
            if (primaryInstaller is not null)
            {
                preloadTasks.Add(RunStepAsync(context, $"预下载 {primary.Key}", $"正在并行下载 {primary.Key} 安装文件", step =>
                {
                    AttachProgressReporter(primaryInstaller, step);
                    return RunInBackgroundAsync(token => PreloadInstallerAsync(primaryInstaller, token), step.CancellationToken);
                }));
            }
            if (optifineInstaller is not null)
            {
                preloadTasks.Add(RunStepAsync(context, "预下载 OptiFine", "正在并行下载 OptiFine 安装包", step =>
                {
                    AttachProgressReporter(optifineInstaller, step);
                    return RunInBackgroundAsync(optifineInstaller.PreloadAsync, step.CancellationToken);
                }));
            }

            await Task.WhenAll([vanillaTask, .. preloadTasks]);
            var minecraft = await vanillaTask;
            if (primaryInstaller is not null)
            {
                minecraft = await RunInstallerStepAsync(context, $"安装 {primary.Key}", $"正在安装最新版 {primary.Key}", primaryInstaller);
            }

            if (optifineInstaller is not null)
            {
                var installer = primaryInstaller is not null
                    ? OptifineInstaller.Create(metaRoot, (OptifineInstallEntry)optifineEntry!, minecraft)
                    : OptifineInstaller.Create(metaRoot, javaPath!, (OptifineInstallEntry)optifineEntry!, effectiveLoaderId);
                minecraft = await RunInstallerStepAsync(context, "安装 OptiFine", "正在安装最新版 OptiFine", installer);
            }

            await RunStepAsync(context, "创建游戏实例", "正在生成实例配置", step =>
            {
                Directory.CreateDirectory(instancesRoot);
                if (hasLoaders)
                {
                    // 将加载器版本从 meta/versions 移入 instances，并把版本 json 重命名为实例名。
                    var loaderVersionDirectory = Path.Combine(metaRoot, "versions", effectiveLoaderId);
                    if (Directory.Exists(loaderVersionDirectory))
                    {
                        Directory.Move(loaderVersionDirectory, instanceDirectory);
                        if (!effectiveLoaderId.Equals(versionId, StringComparison.OrdinalIgnoreCase))
                        {
                            var jsonFile = Path.Combine(instanceDirectory, $"{effectiveLoaderId}.json");
                            if (File.Exists(jsonFile))
                                File.Move(jsonFile, Path.Combine(instanceDirectory, $"{versionId}.json"));
                            var jarFile = Path.Combine(instanceDirectory, $"{effectiveLoaderId}.jar");
                            if (File.Exists(jarFile))
                                File.Move(jarFile, Path.Combine(instanceDirectory, $"{versionId}.jar"));
                        }
                    }
                }
                else
                {
                    WritePortalMcMinimalInstanceJson(instanceDirectory, versionId, vanillaId);
                }
                step.ReportProgress(1);
                return Task.CompletedTask;
            });

            await RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的新实例", step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.SetDescription($"已刷新实例列表，{minecraft.Id} 已可用");
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription($"已完成 Minecraft Java {minecraft.Id} 的安装");
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[MinecraftInstall] Installation {versionId} was cancelled: {exception}");
            await DeleteVersionDirectoryAsync(instanceDirectory);
            if (hasLoaders && !effectiveLoaderId.Equals(versionId, StringComparison.OrdinalIgnoreCase))
                await DeleteVersionDirectoryAsync(Path.Combine(metaRoot, "versions", effectiveLoaderId));
            if (!vanillaDirectoryExisted && !hasLoaders)
                await DeleteVersionDirectoryAsync(vanillaDirectory);
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            await DeleteVersionDirectoryAsync(instanceDirectory);
            if (hasLoaders && !effectiveLoaderId.Equals(versionId, StringComparison.OrdinalIgnoreCase))
                await DeleteVersionDirectoryAsync(Path.Combine(metaRoot, "versions", effectiveLoaderId));
            if (!vanillaDirectoryExisted && !hasLoaders)
                await DeleteVersionDirectoryAsync(vanillaDirectory);
            throw;
        }
    }

    /// <summary>
    /// 写入 Portal MC 纯原版实例的极简继承 json：游戏文件全部来自 meta/versions 与 meta/assets。
    /// </summary>
    public static void WritePortalMcMinimalInstanceJson(string instanceDirectory, string instanceId, string vanillaId)
    {
        Directory.CreateDirectory(instanceDirectory);
        var jsonPath = Path.Combine(instanceDirectory, $"{instanceId}.json");
        using var stream = File.Create(jsonPath);
        using var writer = new System.Text.Json.Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("id", instanceId);
        writer.WriteString("inheritsFrom", vanillaId);
        writer.WriteString("mainClass", "net.minecraft.client.main.Main");
        writer.WritePropertyName("libraries");
        writer.WriteStartArray();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static async Task RunTraditionalInstallationAsync(TaskExecutionContext context, VersionManifestEntry vanilla,
        MinecraftFolderEntry folder, string versionId,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        await RunStepAsync(context, "验证安装配置", "正在检查安装目录、实例 ID 和 Java 运行时", async step =>
        {
            if (RequiresJavaRuntime(selectedEntries.Keys) && string.IsNullOrWhiteSpace(javaPath))
            {
                var runtime = await JavaAutoInstallCoordinator.EnsureAsync(GetRecommendedJavaVersion(vanilla.Id),
                    progress => ReportJavaInstallProgress(step, progress), step.CancellationToken);
                javaPath = runtime?.JavaPath;
                if (string.IsNullOrWhiteSpace(javaPath))
                    throw new InvalidOperationException("所选安装方案需要有效的 Java 运行时。");
            }
            if (Directory.Exists(Path.Combine(folder.FolderPath, "versions", versionId)))
                throw new InvalidOperationException($"实例 ID “{versionId}”已存在于所选文件夹，请更换名称。");
            step.ReportProgress(1);
            await Task.CompletedTask;
        });

        var versionDirectory = Path.Combine(folder.FolderPath, "versions", versionId);
        // Mod-loader profiles must always inherit from the canonical vanilla ID.
        var vanillaId = selectedEntries.Count > 0 ? vanilla.Id : versionId;
        var vanillaDirectory = Path.Combine(folder.FolderPath, "versions", vanillaId);
        var vanillaDirectoryExisted = Directory.Exists(vanillaDirectory);
        try
        {
            var primary = selectedEntries.FirstOrDefault(x => x.Key != LoaderKind.OptiFine);
            var primaryEntry = primary.Value;
            var primaryInstaller = primaryEntry is null
                ? null
                : CreatePrimaryInstaller(primary.Key, primaryEntry, folder.FolderPath, versionId, javaPath);
            var optifineInstaller = selectedEntries.TryGetValue(LoaderKind.OptiFine, out var optifineEntry)
                ? CreatePreloadOptifineInstaller(folder.FolderPath, (OptifineInstallEntry)optifineEntry, javaPath)
                : null;

            var vanillaTask = RunStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {vanilla.Id}", async step =>
            {
                var installer = VanillaInstaller.Create(folder.FolderPath, vanilla, vanillaId);
                AttachProgressReporter(installer, step);
                return await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
            });
            var preloadTasks = new List<Task>();
            if (primaryInstaller is not null)
            {
                preloadTasks.Add(RunStepAsync(context, $"预下载 {primary.Key}", $"正在并行下载 {primary.Key} 安装文件", step =>
                {
                    AttachProgressReporter(primaryInstaller, step);
                    return RunInBackgroundAsync(token => PreloadInstallerAsync(primaryInstaller, token), step.CancellationToken);
                }));
            }
            if (optifineInstaller is not null)
            {
                preloadTasks.Add(RunStepAsync(context, "预下载 OptiFine", "正在并行下载 OptiFine 安装包", step =>
                {
                    AttachProgressReporter(optifineInstaller, step);
                    return RunInBackgroundAsync(optifineInstaller.PreloadAsync, step.CancellationToken);
                }));
            }

            await Task.WhenAll([vanillaTask, .. preloadTasks]);
            var minecraft = await vanillaTask;
            if (primaryInstaller is not null)
            {
                minecraft = await RunInstallerStepAsync(context, $"安装 {primary.Key}", $"正在安装最新版 {primary.Key}", primaryInstaller);
            }

            if (optifineInstaller is not null)
            {
                var installer = primaryInstaller is not null
                    ? OptifineInstaller.Create(folder.FolderPath, (OptifineInstallEntry)optifineEntry!, minecraft)
                    : OptifineInstaller.Create(folder.FolderPath, javaPath!, (OptifineInstallEntry)optifineEntry!, versionId);
                minecraft = await RunInstallerStepAsync(context, "安装 OptiFine", "正在安装最新版 OptiFine", installer);
            }

            await RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的新实例", step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.SetDescription($"已刷新实例列表，{minecraft.Id} 已可用");
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription($"已完成 Minecraft Java {minecraft.Id} 的安装");
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[MinecraftInstall] Installation {versionId} was cancelled: {exception}");
            await DeleteVersionDirectoryAsync(versionDirectory);
            if (!vanillaDirectoryExisted && vanillaDirectory != versionDirectory)
                await DeleteVersionDirectoryAsync(vanillaDirectory);
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            await DeleteVersionDirectoryAsync(versionDirectory);
            if (!vanillaDirectoryExisted && vanillaDirectory != versionDirectory)
                await DeleteVersionDirectoryAsync(vanillaDirectory);
            throw;
        }
    }

    private static Task DeleteVersionDirectoryAsync(string directory) => Task.Run(() =>
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[MinecraftInstall] Failed to clean up {directory}: {exception}");
            // Preserve the original installation or cancellation error.
        }
    });

    private static InstallerBase CreatePrimaryInstaller(LoaderKind kind, IInstallEntry entry, string folder, string versionId,
        string? javaPath) =>
        kind switch
        {
            LoaderKind.Forge or LoaderKind.NeoForge =>
                ForgeInstaller.Create(folder, javaPath!, (ForgeInstallEntry)entry, versionId),
            LoaderKind.Fabric => FabricInstaller.Create(folder, (FabricInstallEntry)entry, versionId),
            LoaderKind.Quilt => QuiltInstaller.Create(folder, (QuiltInstallEntry)entry, versionId),
            _ => throw new InvalidOperationException($"不支持的加载器：{kind}")
        };

    private static OptifineInstaller CreatePreloadOptifineInstaller(string folder, OptifineInstallEntry entry, string? javaPath) =>
        OptifineInstaller.Create(folder, javaPath!, entry);

    private static Task PreloadInstallerAsync(InstallerBase installer, CancellationToken cancellationToken) => installer switch
    {
        ForgeInstaller forge => forge.PreloadAsync(cancellationToken),
        FabricInstaller fabric => fabric.PreloadAsync(cancellationToken),
        QuiltInstaller quilt => quilt.PreloadAsync(cancellationToken),
        _ => throw new NotSupportedException($"不支持预下载的加载器：{installer.GetType().Name}")
    };

    private static async Task<MinecraftEntry> RunInstallerStepAsync(TaskExecutionContext context, string name, string description,
        InstallerBase installer) => await RunStepAsync(context, name, description, async step =>
    {
        AttachProgressReporter(installer, step);
        return await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
    });

    internal static Task RunInBackgroundAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        Task.Run(() => operation(cancellationToken), cancellationToken);

    internal static Task<T> RunInBackgroundAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) => Task.Run(() => operation(cancellationToken), cancellationToken);

    internal static void AttachProgressReporter(InstallerBase installer, TaskExecutionContext context) =>
        installer.ProgressChanged += CreateProgressReporter(context);

    private static EventHandler<InstallProgressChangedEventArgs> CreateProgressReporter(TaskExecutionContext context)
    {
        InstallProgressChangedEventArgs? latestProgress = null;
        var dispatchQueued = 0;
        return (_, progress) =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

            Volatile.Write(ref latestProgress, progress);
            if (Interlocked.Exchange(ref dispatchQueued, 1) != 0) return;

            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dispatchQueued, 0);
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is { } current)
                    ReportInstallerProgress(context, current);
            }, DispatcherPriority.Background);
        };
    }

    internal static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task> operation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var step = context.CreateChild(new TaskOptions { Name = name, Description = description, Progress = 0 }, operation);
        step.Start();
        await step.Completion;
        if (step.Exception is not null) throw new InvalidOperationException(step.Exception.Message, step.Exception);
        context.CancellationToken.ThrowIfCancellationRequested();
    }

    internal static async Task<T> RunStepAsync<T>(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task<T>> operation)
    {
        T? result = default;
        await RunStepAsync(context, name, description, async step =>
        {
            result = await operation(step);
        });
        return result!;
    }

    private static void ReportInstallerProgress(TaskExecutionContext context, InstallProgressChangedEventArgs progress)
    {
        context.ReportProgress(progress.Progress);
        var count = progress.TotalStepTaskCount > 0
            ? $" {progress.FinishedStepTaskCount}/{progress.TotalStepTaskCount}"
            : string.Empty;
        var speed = progress.IsStepSupportSpeed && progress.Speed >= 0
            ? $"，{FormatDownloadSpeed(progress.Speed)}"
            : string.Empty;
        context.SetDescription($"{GetInstallStepDescription(progress.StepName)}{count}{speed}");
    }

    internal static void ReportJavaInstallProgress(TaskExecutionContext context, JavaInstallProgress progress)
    {
        if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
            try
            {
                context.ReportProgress(progress.Fraction);
                context.SetDescription(progress.SpeedBytesPerSecond > 0
                    ? $"{progress.Stage}，下载速度：{DefaultDownloader.FormatSize(progress.SpeedBytesPerSecond, true)}"
                    : progress.Stage);
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private static string GetInstallStepDescription(InstallStep step) => step switch
    {
        InstallStep.Started => "正在准备安装器",
        InstallStep.DownloadVersionJson => "正在下载版本元数据",
        InstallStep.ParseMinecraft => "正在解析 Minecraft 版本信息",
        InstallStep.DownloadAssetIndexFile => "正在下载资源索引",
        InstallStep.DownloadLibraries => "正在下载游戏依赖文件",
        InstallStep.DownloadPackage => "正在下载加载器安装包",
        InstallStep.ParsePackage => "正在解析加载器安装包",
        InstallStep.WriteVersionJsonAndSomeDependencies => "正在写入版本与依赖配置",
        InstallStep.RunInstallProcessor => "正在运行加载器安装处理器",
        InstallStep.RanToCompletion => "安装文件已完成",
        InstallStep.Interrupted => "安装已中断",
        _ => "正在安装游戏文件"
    };

    private static string FormatDownloadSpeed(double bytesPerSecond)
    {
        string[] units = ["B/s", "KiB/s", "MiB/s", "GiB/s"];
        var value = bytesPerSecond;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private bool IsSelected(LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => IsFabricSelected,
        LoaderKind.Forge => IsForgeSelected,
        LoaderKind.NeoForge => IsNeoForgeSelected,
        LoaderKind.Quilt => IsQuiltSelected,
        LoaderKind.OptiFine => IsOptiFineSelected,
        _ => false
    };

    private void SetStatus(LoaderKind kind, string status)
    {
        switch (kind)
        {
            case LoaderKind.Fabric: FabricStatus = status; break;
            case LoaderKind.Forge: ForgeStatus = status; break;
            case LoaderKind.NeoForge: NeoForgeStatus = status; break;
            case LoaderKind.Quilt: QuiltStatus = status; break;
            case LoaderKind.OptiFine: OptiFineStatus = status; break;
        }
    }

    internal static string GetLoaderVersion(LoaderKind kind, IInstallEntry entry) => kind switch
    {
        LoaderKind.Quilt => ((QuiltInstallEntry)entry).Loader.Version,
        _ => entry.DisplayVersion
    };

    private bool SelectedLoadersAreReady() => Enum.GetValues<LoaderKind>()
        .Where(IsSelected).All(_selectedLoaders.ContainsKey);

    private string EffectiveVersionId() => CustomVersionId.Trim();
    private bool IsVersionIdValid() => !_errors.ContainsKey(nameof(CustomVersionId));

    private void UpdateVersionState()
    {
        var id = EffectiveVersionId();
        var error = string.IsNullOrWhiteSpace(id)
            ? "实例 id 不能为空"
            : id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                ? "实例 id 包含文件夹名称不允许的字符"
                : SelectedMinecraftFolder is not null && VersionDirectoryExists(id)
                    ? "该实例 id 已存在，请更换名称"
                    : null;
        SetError(nameof(CustomVersionId), error);
        OnPropertyChanged(nameof(HasModLoader));
        OnPropertyChanged(nameof(CanCustomizeVersionId));
        OnPropertyChanged(nameof(RequiresJava));
        OnPropertyChanged(nameof(HasMissingRequiredJavaRuntime));
        OnPropertyChanged(nameof(CanSelectLoaderVersion));
        OnPropertyChanged(nameof(CanInstall));
    }

    private bool VersionDirectoryExists(string id) => SelectedMinecraftFolder is not null &&
        Directory.Exists(SelectedMinecraftFolder.DetectedLayout.Kind == MinecraftFolderKind.PortalMc
            ? Path.Combine(SelectedMinecraftFolder.FolderPath, "instances", id)
            : Path.Combine(SelectedMinecraftFolder.FolderPath, "versions", id));

    private string CreateRecommendedVersionId()
    {
        var names = Enum.GetValues<LoaderKind>()
            .Where(IsSelected)
            .Select(kind => _selectedLoaders.TryGetValue(kind, out var entry)
                ? $"{kind}-{GetLoaderVersion(kind, entry)}"
                : kind.ToString());
        return HasModLoader ? $"{_vanilla.Id} {string.Join(" + ", names)}" : _vanilla.Id;
    }

    internal static string? GetJavaPath()
    {
        var preferred = Data.ConfigEntry.DefaultJavaRuntime;
        if (preferred is { JavaPath: { } path } && File.Exists(path)) return path;
        return Data.ConfigEntry.JavaRuntimes.Select(runtime => runtime.JavaPath).FirstOrDefault(File.Exists);
    }

    internal static int GetRecommendedJavaVersion(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.', '-', '_');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) || major != 1)
            return 21;
        if (minor >= 21) return 21;
        if (minor == 20 && parts.Length > 2 && int.TryParse(parts[2], out var patch) && patch >= 5) return 21;
        if (minor >= 18) return 17;
        if (minor == 17) return 16;
        return 8;
    }

    public bool HasErrors => _errors.Count > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public void Complete() => RequestClose?.Invoke(this, new MinecraftInstallationDialogResult());
    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errors.TryGetValue(propertyName, out var errors) ? errors : [];

    private void SetError(string propertyName, string? error)
    {
        if (error is null) _errors.Remove(propertyName);
        else _errors[propertyName] = [error];
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
    }
}

public enum LoaderKind
{
    Fabric,
    Forge,
    NeoForge,
    Quilt,
    OptiFine
}
