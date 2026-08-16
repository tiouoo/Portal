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
using Portal.Core.App.Helpers;
using Portal.Core.Helpers;
using Portal.Core.Minecraft;
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

        public static ManagedTask CreateInstallationTask(VersionManifestEntry vanilla, MinecraftFolderEntry folder,
        string versionId, IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath) =>
        MinecraftInstallationTasks.CreateInstallationTask(vanilla, folder, versionId, selectedEntries, javaPath);

    internal static bool RequiresJavaRuntime(IEnumerable<LoaderKind> kinds) =>
        MinecraftInstallationTasks.RequiresJavaRuntime(kinds);

    public static void WritePortalMcMinimalInstanceJson(string instanceDirectory, string instanceId, string vanillaId) =>
        MinecraftInstallationTasks.WritePortalMcMinimalInstanceJson(instanceDirectory, instanceId, vanillaId);

    internal static Task RunInBackgroundAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        MinecraftInstallationTasks.RunInBackgroundAsync(operation, cancellationToken);

    internal static Task<T> RunInBackgroundAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) => MinecraftInstallationTasks.RunInBackgroundAsync(operation, cancellationToken);

    internal static void AttachProgressReporter(InstallerBase installer, TaskExecutionContext context) =>
        MinecraftInstallationTasks.AttachProgressReporter(installer, context);

    internal static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task> operation) =>
        await MinecraftInstallationTasks.RunStepAsync(context, name, description, operation);

    internal static async Task<T> RunStepAsync<T>(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task<T>> operation) =>
        await MinecraftInstallationTasks.RunStepAsync(context, name, description, operation);

    internal static void ReportJavaInstallProgress(TaskExecutionContext context, JavaInstallProgress progress) =>
        MinecraftInstallationTasks.ReportJavaInstallProgress(context, progress);

    internal static string GetLoaderVersion(LoaderKind kind, IInstallEntry entry) =>
        MinecraftInstallationTasks.GetLoaderVersion(kind, entry);

    internal static string? GetJavaPath() => MinecraftInstallationTasks.GetJavaPath();

    internal static int GetRecommendedJavaVersion(string minecraftVersion) =>
        MinecraftInstallationTasks.GetRecommendedJavaVersion(minecraftVersion);

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

