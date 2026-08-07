using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using Portal.Const;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using TioUi.Common;
using TioUi.Controls;
using TioUi.Common.Extensions;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

internal partial class VersionModifyDialog : UserControl
{
    public VersionModifyDialog(MinecraftInstance instance)
    {
        InitializeComponent();
        DataContext = new VersionModifyDialogViewModel(instance);
        // 可编辑 ComboBox 不会在输入时自动弹出下拉列表，这里在文本变化时打开，
        // 让用户输入关键字即可看到过滤后的版本。空引用时安全跳过，避免对话框无法打开。
        if (VersionCombo is { } combo)
            combo.PropertyChanged += (_, e) =>
            {
                if (e.Property != ComboBox.TextProperty) return;
                if (combo.IsDropDownOpen) return;
                if (combo.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(combo.Text))
                    combo.IsDropDownOpen = true;
            };
    }

    private void VersionCombo_OnTextInput(object? sender, TextInputEventArgs e)
    {
        // 键盘输入视为“正在筛选”，由 ViewModel 区分“点开展示全部”与“输入后过滤”。
        if (DataContext is VersionModifyDialogViewModel viewModel)
            viewModel.NotifyVersionTextInput();
    }

    private async void SelectLoaderVersion_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VersionModifyDialogViewModel viewModel)
            await viewModel.SelectLoaderVersionAsync(this);
    }

    private void Modify_OnClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = (VersionModifyDialogViewModel)DataContext!;
        _ = viewModel.ModifyAsync();
        viewModel.Complete();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) =>
        (DataContext as VersionModifyDialogViewModel)?.Cancel();
}

public sealed record VersionModifyDialogResult;

public partial class VersionModifyDialogViewModel : ObservableObject, IDialogContext, IDisposable
{
    private readonly MinecraftInstance _instance;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Dictionary<LoaderKind, IInstallEntry> _selectedLoaders = [];
    private readonly Dictionary<LoaderKind, IReadOnlyList<IInstallEntry>> _availableLoaderVersions = [];
    private readonly Dictionary<LoaderKind, string> _installedLoaderVersions = [];
    private readonly Dictionary<LoaderKind, int> _loadGenerations = [];
    private readonly Dictionary<(string Version, LoaderKind Kind), IReadOnlyList<IInstallEntry>> _loaderOptionsCache = [];
    private readonly List<VersionOption> _categoryVersions = [];
    private VersionManifestEntry? _vanilla;
    private List<MinecraftVersionListItem> _javaVersions = [];
    private int _versionLoadGeneration;
    private bool _userTyping;
    private bool _isSyncingVersionText;
    private bool _versionRefreshQueued;
    private bool _updatingSelection;
    private int _loadingCount;
    private bool _disposed;
    private bool _isModifying;

    public string InstanceName => _instance.InstanceName;

    public string CurrentVersionText
    {
        get
        {
            var entry = _instance.MinecraftEntry;
            if (entry is null) return string.Empty;
            var current = entry.Version.VersionId;
            return entry is ModifiedMinecraftEntry { InheritedMinecraft: { } inherited }
                ? $"当前版本：{current}（基于原版 {inherited.Version.VersionId}）"
                : $"当前版本：{current}";
        }
    }

    public ObservableCollection<VersionOption> Versions { get; } = [];

    public IReadOnlyList<VersionFilterOption> VersionFilters { get; } =
    [
        new("正式版", VersionFilterKind.JavaRelease),
        new("快照版", VersionFilterKind.JavaSnapshot),
        new("愚人节版", VersionFilterKind.JavaAprilFools),
        new("Beta版", VersionFilterKind.JavaBeta)
    ];

    [ObservableProperty] public partial VersionFilterOption? SelectedVersionFilter { get; set; }
    [ObservableProperty] public partial string VersionSearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsVersionDropDownOpen { get; set; }
    [ObservableProperty] public partial VersionOption? SelectedVersion { get; set; }
    [ObservableProperty] public partial bool IsVersionsLoading { get; set; }
    partial void OnIsVersionsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsVersionComboEnabled));

    [ObservableProperty] public partial string VersionsPlaceholder { get; set; } = "加载中...";
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
    public bool RequiresJava => IsForgeSelected || IsNeoForgeSelected || IsOptiFineSelected;
    public bool IsVersionComboEnabled => !IsVersionsLoading;
    public bool CanSelectLoaderVersion => HasModLoader && _loadingCount == 0 && SelectedLoadersAreReady();
    public bool CanModify => !_isModifying && _loadingCount == 0 && !IsVersionsLoading &&
                             SelectedVersion?.Value is VersionManifestEntry && SelectedLoadersAreReady() &&
                             !HasDependentWarning;

    /// <summary>当前实例被其他版本依赖时的提示，修改会破坏这些版本。</summary>
    public string DependentWarning { get; }
    public bool HasDependentWarning => DependentWarning.Length > 0;

    /// <summary>修改任务已启动后的句柄，供调用方在完成后刷新实例页面。</summary>
    public ManagedTask? StartedTask { get; private set; }

    public VersionModifyDialogViewModel(MinecraftInstance instance)
    {
        _instance = instance;
        SelectedVersionFilter = VersionFilters[0];
        PreselectInstalledLoaders();
        DependentWarning = BuildDependentWarning(instance);
        UpdateVersionState();
    }

    private static string BuildDependentWarning(MinecraftInstance instance)
    {
        if (instance.MinecraftEntry is not { } entry)
            return string.Empty;

        var dependents = VersionModifyService.FindDependentVersionIds(entry.MinecraftFolderPath, entry.Id);
        return dependents.Count == 0
            ? string.Empty
            : $"该实例被其他版本依赖（{string.Join("、", dependents)}），修改本实例会破坏它们。" +
              "请改为修改依赖链末端的实例（如加载器实例），或先删除这些依赖版本。";
    }

    /// <summary>
    /// 根据实例已安装的加载器预选对应复选框，并记录已安装版本用于匹配加载器版本列表。
    /// </summary>
    private void PreselectInstalledLoaders()
    {
        if (_instance.MinecraftEntry is not ModifiedMinecraftEntry { ModLoaders: { } modLoaders })
            return;

        foreach (var loader in modLoaders)
        {
            var kind = loader.Type switch
            {
                ModLoaderType.Fabric => LoaderKind.Fabric,
                ModLoaderType.Forge => LoaderKind.Forge,
                ModLoaderType.NeoForge => LoaderKind.NeoForge,
                ModLoaderType.Quilt => LoaderKind.Quilt,
                ModLoaderType.OptiFine => LoaderKind.OptiFine,
                _ => (LoaderKind?)null
            };
            if (kind is not { } selectedKind) continue;

            _installedLoaderVersions[selectedKind] = loader.Version;
            switch (selectedKind)
            {
                case LoaderKind.Fabric: IsFabricSelected = true; break;
                case LoaderKind.Forge: IsForgeSelected = true; break;
                case LoaderKind.NeoForge: IsNeoForgeSelected = true; break;
                case LoaderKind.Quilt: IsQuiltSelected = true; break;
                case LoaderKind.OptiFine: IsOptiFineSelected = true; break;
            }
        }
    }

    partial void OnSelectedVersionFilterChanged(VersionFilterOption? value)
    {
        _categoryVersions.Clear();
        Versions.Clear();
        SelectedVersion = null;
        VersionSearchText = string.Empty;
        if (value is not null)
            _ = EnsureVersionsLoadedAsync();
        UpdateVersionState();
    }

    partial void OnVersionSearchTextChanged(string value)
    {
        // 可编辑 ComboBox 在输入时会在其内部 TextChanged 中自行处理选中项；
        // 若在此时同步重建 ItemsSource，会与它的选择逻辑互相干扰，
        // 导致 ItemsSourceView 索引越界崩溃。这里延迟到输入事件处理完之后再刷新。
        if (IsVersionDropDownOpen && !_isSyncingVersionText)
            _userTyping = true;
        QueueVersionRefresh();
    }

    partial void OnIsVersionDropDownOpenChanged(bool value)
    {
        // 下拉关闭时结束“正在输入”状态，下次点开默认展示全部。
        if (!value)
            _userTyping = false;
        QueueVersionRefresh();
        UpdateVersionState();
    }

    partial void OnSelectedVersionChanged(VersionOption? value)
    {
        if (value is not null)
        {
            // 程序化同步文本不算用户输入，避免把“点选”误判为“正在筛选”。
            _isSyncingVersionText = true;
            try
            {
                VersionSearchText = value.DisplayText;
            }
            finally
            {
                _isSyncingVersionText = false;
            }
        }

        _vanilla = value?.Value as VersionManifestEntry;
        // 更换目标版本后，已选加载器的条目可能不再适用，重新获取最新版。
        foreach (var kind in Enum.GetValues<LoaderKind>().Where(IsSelected))
            _ = LoadLatestAsync(kind);
        UpdateVersionState();
    }

    partial void OnIsFabricSelectedChanged(bool value) => SelectionChanged(LoaderKind.Fabric, value);
    partial void OnIsForgeSelectedChanged(bool value) => SelectionChanged(LoaderKind.Forge, value);
    partial void OnIsNeoForgeSelectedChanged(bool value) => SelectionChanged(LoaderKind.NeoForge, value);
    partial void OnIsQuiltSelectedChanged(bool value) => SelectionChanged(LoaderKind.Quilt, value);
    partial void OnIsOptiFineSelectedChanged(bool value) => SelectionChanged(LoaderKind.OptiFine, value);

    /// <summary>键盘输入关键字时视为“正在筛选”，由 ViewModel 区分“点开展示全部”与“输入后过滤”。</summary>
    public void NotifyVersionTextInput() => _userTyping = true;

    private void QueueVersionRefresh()
    {
        if (_versionRefreshQueued) return;
        _versionRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _versionRefreshQueued = false;
            if (_disposed) return;
            RefreshVersionList();
        }, DispatcherPriority.Background);
    }

    private async Task EnsureVersionsLoadedAsync()
    {
        var filter = SelectedVersionFilter;
        if (filter is null) return;
        var generation = ++_versionLoadGeneration;

        IsVersionsLoading = true;
        VersionsPlaceholder = "加载中...";
        Versions.Clear();
        SelectedVersion = null;
        UpdateVersionState();

        try
        {
            if (_javaVersions.Count == 0)
            {
                var entries = Data.UiProperty.MinecraftVersionManifestEntries;
                if (entries.Count == 0)
                {
                    var loaded = await VanillaInstaller.EnumerableMinecraftAsync(_disposeCancellation.Token);
                    if (entries.Count == 0)
                        entries.AddRange(loaded);
                }

                _javaVersions = entries.Select(MinecraftVersionListItem.FromEntry).ToList();
            }

            if (generation != _versionLoadGeneration || _disposed) return;
            PopulateVersions(filter);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (generation != _versionLoadGeneration || _disposed) return;
            IsVersionsLoading = false;
            VersionsPlaceholder = "无法获取版本列表，请检查网络连接";
            UpdateVersionState();
        }
    }

    private void PopulateVersions(VersionFilterOption filter)
    {
        // 实例当前版本不在默认的“正式版”分类时（如快照版实例），自动切换到对应分类，
        // 保证默认选中当前安装的版本。
        var currentId = GetCurrentVanillaId();
        if (currentId is not null &&
            _javaVersions.FirstOrDefault(version =>
                string.Equals(version.Name, currentId, StringComparison.OrdinalIgnoreCase)) is { RawType: var rawType })
        {
            var expectedKind = rawType switch
            {
                "release" => VersionFilterKind.JavaRelease,
                "snapshot" => VersionFilterKind.JavaSnapshot,
                "old_beta" or "old_alpha" => VersionFilterKind.JavaBeta,
                _ => (VersionFilterKind?)null
            };
            if (expectedKind is { } kind && kind != filter.Kind &&
                VersionFilters.FirstOrDefault(item => item.Kind == kind) is { } target)
            {
                SelectedVersionFilter = target;
                return;
            }
        }

        _categoryVersions.Clear();
        var list = _javaVersions
            .Where(version => filter.Kind switch
            {
                VersionFilterKind.JavaRelease => version.RawType == "release",
                VersionFilterKind.JavaSnapshot => version.RawType == "snapshot",
                VersionFilterKind.JavaAprilFools => MinecraftVersionListItem.IsAprilFoolsVersion(version.Name),
                VersionFilterKind.JavaBeta => version.RawType is "old_beta" or "old_alpha",
                _ => false
            })
            .OrderByDescending(version => version.ReleaseTime)
            .ToList();
        foreach (var version in list)
            _categoryVersions.Add(new VersionOption(version.Name, version.Entry!));

        IsVersionsLoading = false;
        // 优先选中实例当前使用的原版版本，方便直接升级/降级到相邻版本。
        SelectedVersion = _categoryVersions.FirstOrDefault(option =>
            string.Equals(option.DisplayText, currentId, StringComparison.OrdinalIgnoreCase))
            ?? _categoryVersions.FirstOrDefault();
        if (SelectedVersion is not null)
            VersionSearchText = SelectedVersion.DisplayText;

        RefreshVersionList();
        UpdateVersionState();
    }

    private string? GetCurrentVanillaId() => _instance.MinecraftEntry switch
    {
        ModifiedMinecraftEntry { InheritedMinecraft: { } inherited } => inherited.Version.VersionId,
        { } entry => entry.Version.VersionId,
        _ => null
    };

    private void RefreshVersionList()
    {
        var query = VersionSearchText.Trim();
        var selected = SelectedVersion;

        // 点开下拉但尚未输入 → 展示该分类下的全部版本；
        // 用户输入（键盘/粘贴）后才按关键字过滤。
        var isFiltering = IsVersionDropDownOpen && _userTyping && query.Length > 0;

        // 重建下拉列表，但绝不能把当前选中项从列表中移除：一旦选中项被移除，
        // ComboBox 会清空输入框文本（UpdateInputTextFromSelection(null) 会把 Text 置空），
        // 从而打断用户正在输入的关键字。
        var keep = new HashSet<VersionOption>(_categoryVersions.Where(version =>
            !isFiltering || version.DisplayText.Contains(query, StringComparison.OrdinalIgnoreCase)));
        if (selected is not null) keep.Add(selected);

        for (var i = Versions.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Versions[i]))
                Versions.RemoveAt(i);
        }

        foreach (var item in keep)
        {
            if (!Versions.Contains(item))
                Versions.Add(item);
        }

        VersionsPlaceholder = IsVersionsLoading
            ? "加载中..."
            : _categoryVersions.Count == 0
                ? "暂无版本"
                : Versions.Count == 0
                    ? "无匹配版本"
                    : "选择游戏版本";

        if (!isFiltering)
        {
            UpdateVersionState();
            return;
        }

        var exact = _categoryVersions.FirstOrDefault(version =>
            string.Equals(version.DisplayText, query, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            if (!ReferenceEquals(selected, exact) ||
                !string.Equals(VersionSearchText, exact.DisplayText, StringComparison.Ordinal))
            {
                SelectedVersion = exact;
                VersionSearchText = exact.DisplayText;
            }
        }
        else if (selected is not null)
        {
            SelectedVersion = null;
        }

        UpdateVersionState();
    }

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
                    // OptiFine 可叠加在任何主加载器之上，不与其他加载器互斥
                }
                else
                {
                    IsFabricSelected = kind == LoaderKind.Fabric;
                    IsForgeSelected = kind == LoaderKind.Forge;
                    IsNeoForgeSelected = kind == LoaderKind.NeoForge;
                    IsQuiltSelected = kind == LoaderKind.Quilt;
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

        UpdateVersionState();
        if (selected && IsSelected(kind)) _ = LoadLatestAsync(kind);
    }

    private async Task LoadLatestAsync(LoaderKind kind)
    {
        var generation = _loadGenerations.GetValueOrDefault(kind) + 1;
        _loadGenerations[kind] = generation;
        _loadingCount++;
        SetStatus(kind, "正在获取最新版...");
        UpdateVersionState();
        try
        {
            if (_vanilla is not { } vanilla) return;
            if (!_loaderOptionsCache.TryGetValue((vanilla.Id, kind), out var entries))
            {
                entries = await FetchVersionsAsync(kind);
                _loaderOptionsCache[(vanilla.Id, kind)] = entries;
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
                // 优先匹配实例当前安装的加载器版本，找不到时才回退到最新版
                var matched = MatchInstalledLoaderVersion(kind, entries);
                var entry = matched ?? entries[0];
                _availableLoaderVersions[kind] = entries;
                _selectedLoaders[kind] = entry;
                SetStatus(kind, matched is not null
                    ? $"已选择当前版本：{MinecraftInstallationViewModel.GetLoaderVersion(kind, entry)}"
                    : $"最新版：{MinecraftInstallationViewModel.GetLoaderVersion(kind, entry)}");
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

    private IInstallEntry? MatchInstalledLoaderVersion(LoaderKind kind, IReadOnlyList<IInstallEntry> entries)
    {
        if (!_installedLoaderVersions.TryGetValue(kind, out var installedVersion))
            return null;

        foreach (var entry in entries)
        {
            var version = MinecraftInstallationViewModel.GetLoaderVersion(kind, entry);
            if (string.Equals(version, installedVersion, StringComparison.OrdinalIgnoreCase))
                return entry;
            // Forge 的安装版本形如“1.21.3-49.0.5”，实例中记录的是“49.0.5”
            if (kind == LoaderKind.Forge &&
                string.Equals(version.Split('-').LastOrDefault(), installedVersion, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        // OptiFine 补丁版本可能带 pre 后缀（如已安装 HD_U_H6 与可用的 HD_U_H6_pre1），允许前缀匹配
        if (kind == LoaderKind.OptiFine)
        {
            foreach (var entry in entries)
            {
                if (MinecraftInstallationViewModel.GetLoaderVersion(kind, entry)
                    .StartsWith(installedVersion, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<IInstallEntry>> FetchVersionsAsync(LoaderKind kind) =>
        await Task.Run(async () => kind switch
        {
            LoaderKind.Fabric => (await FabricInstaller.EnumerableFabricAsync(_vanilla!.Id)).Cast<IInstallEntry>().ToList(),
            LoaderKind.Forge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla!.Id)).Cast<IInstallEntry>().ToList(),
            LoaderKind.NeoForge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla!.Id, true)).Cast<IInstallEntry>().ToList(),
            LoaderKind.Quilt => (await QuiltInstaller.EnumerableQuiltAsync(_vanilla!.Id)).Cast<IInstallEntry>().ToList(),
            LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(_vanilla!.Id)).Cast<IInstallEntry>().ToList(),
            _ => []
        });

    public async Task SelectLoaderVersionAsync(Control owner)
    {
        if (!CanSelectLoaderVersion) return;

        var versions = _availableLoaderVersions
            .Where(pair => IsSelected(pair.Key))
            .SelectMany(pair => pair.Value.Select(entry =>
                new LoaderVersionItem(pair.Key, entry, MinecraftInstallationViewModel.GetLoaderVersion(pair.Key, entry))))
            .ToList();
        var selected = await OverlayDialog.ShowCustomAsync<LoaderVersionDialog, LoaderVersionDialogViewModel, LoaderVersionItem>(
            new LoaderVersionDialogViewModel(versions), owner.GetTopLevel().TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = "选择加载器版本",
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                VerticalAnchor = VerticalPosition.Top,
                VerticalOffset = 80
            });
        if (selected is null || !IsSelected(selected.Kind)) return;

        _selectedLoaders[selected.Kind] = selected.Entry;
        SetStatus(selected.Kind, $"已选择版本：{selected.Version}");
        UpdateVersionState();
    }

    public async Task ModifyAsync()
    {
        if (!CanModify || SelectedVersion?.Value is not VersionManifestEntry vanilla) return;
        _isModifying = true;
        UpdateVersionState();
        var selectedEntries = _selectedLoaders.ToDictionary(x => x.Key, x => x.Value);
        var javaPath = MinecraftInstallationViewModel.GetJavaPath();
        var task = VersionModifyService.CreateModifyTask(_instance, vanilla, selectedEntries, javaPath);
        StartedTask = task;
        task.Start();
        try
        {
            await task.Completion;
        }
        finally
        {
            _isModifying = false;
            UpdateVersionState();
        }
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

    private bool SelectedLoadersAreReady() => Enum.GetValues<LoaderKind>()
        .Where(IsSelected).All(_selectedLoaders.ContainsKey);

    private void UpdateVersionState()
    {
        OnPropertyChanged(nameof(HasModLoader));
        OnPropertyChanged(nameof(RequiresJava));
        OnPropertyChanged(nameof(CanSelectLoaderVersion));
        OnPropertyChanged(nameof(CanModify));
    }

    public void Complete() => RequestClose?.Invoke(this, new VersionModifyDialogResult());
    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCancellation.Cancel();
    }
}
