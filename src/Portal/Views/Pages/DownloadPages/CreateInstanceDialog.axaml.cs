using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class CreateInstanceDialog : UserControl
{
    public CreateInstanceDialog()
    {
        InitializeComponent();
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
        if (DataContext is CreateInstanceDialogViewModel viewModel)
            viewModel.NotifyVersionTextInput();
    }

    private async void ChangeIcon_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CreateInstanceDialogViewModel viewModel) return;

        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
        };
        var result = await OverlayDialog.ShowCustomAsync<IconPicker, IconPickerViewModel, IconPickerResult>(
            new IconPickerViewModel(), this.TryGetHostId(), options);
        if (result is null) return;
        await viewModel.SetPendingIconAsync(result);
    }

    private void ResetIcon_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as CreateInstanceDialogViewModel)?.ResetIcon();

    private void Create_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as CreateInstanceDialogViewModel)?.Create();

    private void Cancel_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as CreateInstanceDialogViewModel)?.Cancel();
}

public enum InstancePlatform
{
    Java,
    Bedrock
}

public sealed record PlatformOption(string DisplayText, InstancePlatform Platform);

public sealed record VersionFilterOption(string DisplayText, VersionFilterKind Kind)
{
    public bool IsJava => Kind is VersionFilterKind.JavaRelease or VersionFilterKind.JavaSnapshot
        or VersionFilterKind.JavaAprilFools or VersionFilterKind.JavaBeta;
}

public enum VersionFilterKind
{
    JavaRelease,
    JavaSnapshot,
    JavaAprilFools,
    JavaBeta,
    BedrockGdkRelease,
    BedrockGdkPreview,
    BedrockUwpRelease,
    BedrockUwpPreview
}

public sealed record VersionOption(string DisplayText, object Value)
{
    // 记录类型的默认 ToString 会打印 Value（VersionManifestEntry/BedrockVersion），
    // 而 VersionManifestEntry.ToString() 会触发未实现的 Description 访问器导致崩溃。
    // ComboBox 在选择变化时会对条目调用 ToString()，这里仅返回展示文本。
    public override string ToString() => DisplayText;
}

public sealed record LoaderOption(string DisplayText, LoaderKind? Kind);

public sealed record LoaderVersionFilterOption(string DisplayText, LoaderVersionFilterKind Kind);

public enum LoaderVersionFilterKind
{
    Stable,
    Latest,
    Other
}

public sealed record LoaderVersionOption(string DisplayText, IInstallEntry Entry)
{
    // 与 VersionOption 同理，避免 ComboBox 对条目调用 ToString 时打印底层安装条目。
    public override string ToString() => DisplayText;
}

public partial class CreateInstanceDialogViewModel : ObservableObject, IDialogContext, IDisposable
{
    private static readonly string DefaultIconResource = "Portal.Core.Assets.McIcons.01_grass_block_side.png";

    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Dictionary<(string Version, LoaderKind Kind), IReadOnlyList<LoaderVersionOption>>
        _loaderOptionsCache = [];
    private bool _disposed;
    private bool _javaVersionsLoaded;
    private bool _bedrockVersionsLoaded;
    private bool _userTyping;
    private bool _isSyncingVersionText;
    private List<MinecraftVersionListItem> _javaVersions = [];
    private List<BedrockVersion> _bedrockVersions = [];
    private readonly List<VersionOption> _categoryVersions = [];
    private int _versionLoadGeneration;
    private int _loaderLoadGeneration;
    private bool _isCreating;
    private string _lastRecommendedInstanceId = string.Empty;
    private IconPickerResult? _pendingIcon;
    private byte[]? _pendingIconData;
    private LoaderKind? _currentLoaderKind;
    private string? _currentMcVersion;
    private IReadOnlyList<LoaderVersionOption>? _currentLoaderOptions;
    private IInstallEntry? _latestLoaderEntry;
    private IInstallEntry? _stableLoaderEntry;
    private bool _hasStableLoader;

    public ObservableCollection<VersionOption> Versions { get; } = [];
    public ObservableCollection<LoaderVersionOption> CustomLoaderVersions { get; } = [];

    public IReadOnlyList<PlatformOption> Platforms { get; } =
    [
        new("Java", InstancePlatform.Java),
        new("基岩", InstancePlatform.Bedrock)
    ];

    // Java 与基岩的类型筛选彼此独立，按所选平台切换。
    private static readonly IReadOnlyList<VersionFilterOption> JavaVersionFilters =
    [
        new("正式版", VersionFilterKind.JavaRelease),
        new("快照版", VersionFilterKind.JavaSnapshot),
        new("愚人节版", VersionFilterKind.JavaAprilFools),
        new("Beta版", VersionFilterKind.JavaBeta)
    ];

    private static readonly IReadOnlyList<VersionFilterOption> BedrockVersionFilters = BuildBedrockVersionFilters();

    public IReadOnlyList<VersionFilterOption> VersionFilters =>
        SelectedPlatform?.Platform == InstancePlatform.Bedrock ? BedrockVersionFilters : JavaVersionFilters;
    public IReadOnlyList<LoaderOption> LoaderOptions { get; } =
    [
        new("不安装", null),
        new("Fabric", LoaderKind.Fabric),
        new("NeoForge", LoaderKind.NeoForge),
        new("Forge", LoaderKind.Forge),
        new("Quilt", LoaderKind.Quilt),
        new("OptiFine", LoaderKind.OptiFine)
    ];
    public IReadOnlyList<LoaderVersionFilterOption> LoaderVersionFilters { get; } =
    [
        new("稳定版", LoaderVersionFilterKind.Stable),
        new("最新版", LoaderVersionFilterKind.Latest),
        new("其他", LoaderVersionFilterKind.Other)
    ];

    public IReadOnlyList<MinecraftFolderEntry> MinecraftFolders { get; }

    [ObservableProperty] public partial MinecraftFolderEntry? SelectedMinecraftFolder { get; set; }
    [ObservableProperty] public partial string InstanceId { get; set; } = string.Empty;
    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CanCustomizeInstanceId { get; set; } = true;
    [ObservableProperty] public partial PlatformOption? SelectedPlatform { get; set; }
    [ObservableProperty] public partial VersionFilterOption? SelectedVersionFilter { get; set; }
    [ObservableProperty] public partial string VersionSearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsVersionDropDownOpen { get; set; }
    [ObservableProperty] public partial VersionOption? SelectedVersion { get; set; }
    [ObservableProperty] public partial bool IsVersionsLoading { get; set; }
    [ObservableProperty] public partial string VersionsPlaceholder { get; set; } = "加载中...";
    [ObservableProperty] public partial bool IsLoaderVisible { get; set; }
    [ObservableProperty] public partial LoaderOption? SelectedLoader { get; set; }
    [ObservableProperty] public partial LoaderVersionFilterOption? SelectedLoaderVersionFilter { get; set; }
    [ObservableProperty] public partial LoaderVersionOption? SelectedCustomLoaderVersion { get; set; }
    [ObservableProperty] public partial bool IsLoaderVersionAreaVisible { get; set; }
    [ObservableProperty] public partial bool IsCustomLoaderVersionsLoading { get; set; }
    [ObservableProperty] public partial string CustomLoaderVersionsPlaceholder { get; set; } = "加载中...";
    [ObservableProperty] public partial string LoaderStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial string ErrorText { get; set; } = string.Empty;
    [ObservableProperty] public partial Bitmap? IconPreview { get; set; }

    public bool IsVersionComboEnabled => !IsVersionsLoading;
    public bool IsCustomLoaderVersionComboEnabled => !IsCustomLoaderVersionsLoading && CustomLoaderVersions.Count > 0;

    /// <summary>仅当选中了某个加载器且版本筛选为“其他”时，才显示自定义加载器版本下拉框。</summary>
    public bool IsCustomLoaderVersionVisible =>
        SelectedLoader?.Kind is not null && SelectedLoaderVersionFilter?.Kind == LoaderVersionFilterKind.Other;
    public bool HasLoaderStatus => !string.IsNullOrEmpty(LoaderStatus);
    public bool HasErrorText => !string.IsNullOrEmpty(ErrorText);
    public bool CanCreate => !_isCreating && SelectedVersion is not null && !IsVersionsLoading &&
                             (SelectedLoader?.Kind is null ||
                              (!IsCustomLoaderVersionsLoading && EffectiveLoaderEntry is not null)) &&
                             string.IsNullOrEmpty(ErrorText);

    private bool IsBedrockFilter => SelectedPlatform?.Platform == InstancePlatform.Bedrock;

    private IInstallEntry? EffectiveLoaderEntry
    {
        get
        {
            if (_currentLoaderKind is null) return null;
            return SelectedLoaderVersionFilter?.Kind switch
            {
                LoaderVersionFilterKind.Stable => _stableLoaderEntry,
                LoaderVersionFilterKind.Latest => _latestLoaderEntry,
                LoaderVersionFilterKind.Other => SelectedCustomLoaderVersion?.Entry,
                _ => null
            };
        }
    }

    public CreateInstanceDialogViewModel()
    {
        MinecraftFolders = Data.ConfigEntry.TraditionalMinecraftFolders.ToList();
        SelectedMinecraftFolder = Data.ConfigEntry.DefaultMinecraftFolder is { DetectedLayout.Kind: MinecraftFolderKind.Standard } folder &&
                                  MinecraftFolders.Contains(folder)
            ? folder
            : MinecraftFolders.FirstOrDefault();
        SelectedPlatform = Platforms[0];
        SelectedLoader = LoaderOptions[0];
        SelectedLoaderVersionFilter = LoaderVersionFilters[0];
        UpdateLoaderIcon();
    }

    partial void OnSelectedPlatformChanged(PlatformOption? value)
    {
        // 平台筛选独立于类型筛选：切换平台时刷新类型列表并重置到默认项。
        OnPropertyChanged(nameof(VersionFilters));
        IsLoaderVisible = value?.Platform == InstancePlatform.Java;
        CanCustomizeInstanceId = value?.Platform == InstancePlatform.Java;
        if (IsLoaderVisible)
        {
            // 切回 Java 时恢复加载器区域状态（可能仍保留之前选的加载器）。
            SyncLoaderState();
        }
        else
        {
            ResetLoaderState();
            InstanceId = string.Empty;
            _lastRecommendedInstanceId = string.Empty;
        }

        SelectedVersionFilter = VersionFilters.FirstOrDefault();
        UpdateLoaderIcon();
    }

    partial void OnSelectedVersionFilterChanged(VersionFilterOption? value)
    {
        _categoryVersions.Clear();
        Versions.Clear();
        SelectedVersion = null;
        VersionSearchText = string.Empty;
        if (value is not null)
            _ = EnsureVersionsLoadedAsync();
        UpdateLoaderIcon();
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

    partial void OnInstanceIdChanged(string value) => UpdateVersionState();

    partial void OnIsVersionDropDownOpenChanged(bool value)
    {
        // 下拉关闭时结束“正在输入”状态，下次点开默认展示全部。
        if (!value)
            _userTyping = false;
        QueueVersionRefresh();
        UpdateVersionState();
    }

    /// <summary>用户通过键盘输入内容（由 ComboBox 的 TextInput 事件触发）。</summary>
    public void NotifyVersionTextInput() => _userTyping = true;

    private bool _versionRefreshQueued;

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

        if (value?.Value is VersionManifestEntry vanilla && SelectedLoader?.Kind is { } kind)
            _ = EnsureLoaderVersionsAsync(kind, vanilla.Id);
        UpdateLoaderIcon();
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedLoaderChanged(LoaderOption? value)
    {
        SyncLoaderState();
        UpdateLoaderIcon();
    }

    /// <summary>根据当前加载器选择同步加载器区域的可见性与版本加载。</summary>
    private void SyncLoaderState()
    {
        ResetLoaderState();
        if (SelectedLoader?.Kind is { } kind)
        {
            IsLoaderVersionAreaVisible = true;
            if (SelectedVersion?.Value is VersionManifestEntry vanilla)
                _ = EnsureLoaderVersionsAsync(kind, vanilla.Id);
            else
                LoaderStatus = "请先选择游戏版本";
        }

        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedLoaderVersionFilterChanged(LoaderVersionFilterOption? value)
    {
        if (SelectedLoaderVersionFilter?.Kind == LoaderVersionFilterKind.Other &&
            _currentLoaderKind is { } kind && _currentMcVersion is { } version)
            _ = EnsureLoaderVersionsAsync(kind, version);
        UpdateLoaderVersionStatus();
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedCustomLoaderVersionChanged(LoaderVersionOption? value)
    {
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedMinecraftFolderChanged(MinecraftFolderEntry? value) => UpdateVersionState();

    partial void OnIsVersionsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsVersionComboEnabled));
    partial void OnIsCustomLoaderVersionsLoadingChanged(bool value) =>
        OnPropertyChanged(nameof(IsCustomLoaderVersionComboEnabled));
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasErrorText));
    partial void OnLoaderStatusChanged(string value) => OnPropertyChanged(nameof(HasLoaderStatus));

    public async Task SetPendingIconAsync(IconPickerResult result)
    {
        _pendingIcon = result;
        try
        {
            using var stream = result.CustomImageFile is not null
                ? await result.CustomImageFile.OpenReadAsync()
                : typeof(MinecraftInstance).Assembly.GetManifestResourceStream(result.BuiltInResourceName!);
            if (stream is null) return;
            // 缓存图标字节：安装完成后用它们给新实例设置图标，
            // 避免依赖对话框关闭后可能失效的 IStorageFile。
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            _pendingIconData = memory.ToArray();
            memory.Position = 0;
            var preview = Bitmap.DecodeToWidth(memory, 72);
            IconPreview?.Dispose();
            IconPreview = preview;
        }
        catch (Exception)
        {
        }
    }

    public void ResetIcon()
    {
        _pendingIcon = null;
        _pendingIconData = null;
        UpdateLoaderIcon();
    }

    /// <summary>根据当前版本类型与所选加载器给出推荐的实例图标资源。</summary>
    private string GetSuggestedIconResource()
    {
        if (IsBedrockFilter)
            return DefaultIconResource;

        if (SelectedLoader?.Kind is { } kind)
        {
            return kind switch
            {
                LoaderKind.Fabric => "Portal.Core.Assets.McIcons.05_FabricIcon.png",
                LoaderKind.Forge => "Portal.Core.Assets.McIcons.06_ForgeIcon.png",
                LoaderKind.NeoForge => "Portal.Core.Assets.McIcons.07_NeoForgeIcon.png",
                LoaderKind.OptiFine => "Portal.Core.Assets.McIcons.08_OptiFineIcon.png",
                LoaderKind.Quilt => "Portal.Core.Assets.McIcons.09_QuiltIcon.png",
                _ => DefaultIconResource
            };
        }

        if (SelectedVersion?.Value is VersionManifestEntry vanilla &&
            string.Equals(vanilla.Type, "snapshot", StringComparison.OrdinalIgnoreCase))
            return "Portal.Core.Assets.McIcons.02_crafting_table_front.png";

        return DefaultIconResource;
    }

    /// <summary>切换版本/加载器时自动更新图标预览；用户手动选过图标则不覆盖。</summary>
    private void UpdateLoaderIcon()
    {
        if (_pendingIconData is not null) return;
        IconPreview?.Dispose();
        IconPreview = LoadIconResource(GetSuggestedIconResource());
    }

    private static Bitmap? LoadIconResource(string resource)
    {
        var assembly = typeof(MinecraftInstance).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource);
        return stream is not null ? Bitmap.DecodeToWidth(stream, 72) : null;
    }

    public void Create()
    {
        if (!CanCreate || _isCreating || SelectedVersion?.Value is not { } selected) return;
        _isCreating = true;
        OnPropertyChanged(nameof(CanCreate));
        Logger.Info($"[CreateInstance] Creating {SelectedPlatform?.Platform} instance {InstanceId.Trim()} in {SelectedMinecraftFolder?.FolderPath} from version {SelectedVersion.DisplayText}.");

        if (selected is VersionManifestEntry vanilla)
            CreateJava(vanilla);
        else if (selected is BedrockVersion bedrock)
            CreateBedrock(bedrock);
    }

    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();

    public event EventHandler<object?>? RequestClose;

    public void Dispose()
    {
        if (_disposed) return;
        Logger.Info("[CreateInstance] Dialog disposed; cancelling pending version and loader requests.");
        _disposed = true;
        _disposeCancellation.Cancel();
    }

    private static IReadOnlyList<VersionFilterOption> BuildBedrockVersionFilters()
    {
        var filters = new List<VersionFilterOption>
        {
            new("GDK正式版", VersionFilterKind.BedrockGdkRelease),
            new("GDK预览版", VersionFilterKind.BedrockGdkPreview)
        };
        if (OperatingSystem.IsWindows())
        {
            filters.Add(new VersionFilterOption("UWP正式版", VersionFilterKind.BedrockUwpRelease));
            filters.Add(new VersionFilterOption("UWP预览版", VersionFilterKind.BedrockUwpPreview));
        }

        return filters;
    }

    private async Task EnsureVersionsLoadedAsync()
    {
        var filter = SelectedVersionFilter;
        if (filter is null) return;
        var generation = ++_versionLoadGeneration;

        IsVersionsLoading = true;
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[CreateInstance] Loading {filter.Kind} version list.");
        VersionsPlaceholder = "加载中...";
        Versions.Clear();
        SelectedVersion = null;
        UpdateVersionState();

        try
        {
            if (filter.IsJava)
                await EnsureJavaVersionsLoadedAsync();
            else
                await EnsureBedrockVersionsLoadedAsync();
            if (generation != _versionLoadGeneration || _disposed) return;
            PopulateVersions(filter);
            Logger.Info($"[CreateInstance] Loaded {_categoryVersions.Count} {filter.Kind} version(s) in {stopwatch.Elapsed}.");
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            Logger.Debug($"[CreateInstance] Loading {filter.Kind} version list was cancelled after {stopwatch.Elapsed}.");
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

    private async Task EnsureJavaVersionsLoadedAsync()
    {
        if (_javaVersionsLoaded) return;
        var entries = Data.UiProperty.MinecraftVersionManifestEntries;
        if (entries.Count == 0)
        {
            var loaded = await VanillaInstaller.EnumerableMinecraftAsync(_disposeCancellation.Token);
            if (entries.Count == 0)
                entries.AddRange(loaded);
        }

        _javaVersions = entries.Select(MinecraftVersionListItem.FromEntry).ToList();
        _javaVersionsLoaded = true;
    }

    private async Task EnsureBedrockVersionsLoadedAsync()
    {
        if (_bedrockVersionsLoaded) return;
        if (BedrockInstallationService.DefaultInstaller is not { } installer)
        {
            _bedrockVersions = [];
            _bedrockVersionsLoaded = true;
            return;
        }

        var versions = await installer.GetVersionsAsync(false, _disposeCancellation.Token);
        _bedrockVersions = versions.ToList();
        _bedrockVersionsLoaded = true;
    }

    private void PopulateVersions(VersionFilterOption filter)
    {
        _categoryVersions.Clear();
        if (filter.IsJava)
        {
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
        }
        else
        {
            var list = _bedrockVersions
                .Where(version => filter.Kind switch
                {
                    VersionFilterKind.BedrockGdkRelease =>
                        version.BuildType == BedrockBuildType.GDK && !version.IsPreview,
                    VersionFilterKind.BedrockGdkPreview =>
                        version.BuildType == BedrockBuildType.GDK && version.IsPreview,
                    VersionFilterKind.BedrockUwpRelease =>
                        version.BuildType == BedrockBuildType.UWP && !version.IsPreview,
                    VersionFilterKind.BedrockUwpPreview =>
                        version.BuildType == BedrockBuildType.UWP && version.IsPreview,
                    _ => false
                })
                .OrderByDescending(version => version.ReleaseTime)
                .ToList();
            foreach (var version in list)
                _categoryVersions.Add(new VersionOption(version.Id, version));
        }

        IsVersionsLoading = false;
        if (_categoryVersions.Count > 0 && SelectedVersion is null)
        {
            SelectedVersion = _categoryVersions[0];
            VersionSearchText = _categoryVersions[0].DisplayText;
        }

        RefreshVersionList();
        UpdateVersionState();
    }

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

    private async Task EnsureLoaderVersionsAsync(LoaderKind kind, string mcVersion)
    {
        var generation = ++_loaderLoadGeneration;
        if (_loaderOptionsCache.TryGetValue((mcVersion, kind), out var cached))
        {
            ApplyLoaderOptions(kind, mcVersion, cached);
            return;
        }

        IsCustomLoaderVersionsLoading = true;
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[CreateInstance] Loading {kind} versions for Minecraft {mcVersion}.");
        CustomLoaderVersionsPlaceholder = "加载中...";
        UpdateVersionState();
        try
        {
            var entries = await FetchLoaderVersionsAsync(kind, mcVersion);
            var options = entries.Select(entry => new LoaderVersionOption(GetLoaderVersion(kind, entry), entry)).ToList();
            _loaderOptionsCache[(mcVersion, kind)] = options;
            if (generation != _loaderLoadGeneration || _disposed) return;
            ApplyLoaderOptions(kind, mcVersion, options);
            Logger.Info($"[CreateInstance] Loaded {options.Count} {kind} version(s) for Minecraft {mcVersion} in {stopwatch.Elapsed}.");
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            Logger.Debug($"[CreateInstance] Loading {kind} versions for Minecraft {mcVersion} was cancelled after {stopwatch.Elapsed}.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (generation != _loaderLoadGeneration || _disposed) return;
            LoaderStatus = "获取加载器版本失败，请重试";
        }
        finally
        {
            if (generation == _loaderLoadGeneration)
            {
                IsCustomLoaderVersionsLoading = false;
                UpdateVersionState();
            }
        }
    }

    private static async Task<IReadOnlyList<IInstallEntry>> FetchLoaderVersionsAsync(LoaderKind kind, string mcVersion) =>
        kind switch
        {
            LoaderKind.Fabric => (await FabricInstaller.EnumerableFabricAsync(mcVersion)).Cast<IInstallEntry>().ToList(),
            LoaderKind.Forge => (await ForgeInstaller.EnumerableForgeAsync(mcVersion)).Cast<IInstallEntry>().ToList(),
            LoaderKind.NeoForge => (await ForgeInstaller.EnumerableForgeAsync(mcVersion, true)).Cast<IInstallEntry>().ToList(),
            LoaderKind.Quilt => (await QuiltInstaller.EnumerableQuiltAsync(mcVersion)).Cast<IInstallEntry>().ToList(),
            LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(mcVersion)).Cast<IInstallEntry>().ToList(),
            _ => []
        };

    private void ApplyLoaderOptions(LoaderKind kind, string mcVersion, IReadOnlyList<LoaderVersionOption> options)
    {
        _currentLoaderKind = kind;
        _currentMcVersion = mcVersion;
        _currentLoaderOptions = options;
        _latestLoaderEntry = options.FirstOrDefault()?.Entry;
        _hasStableLoader = options.Any(option => IsStableLoader(kind, option.Entry));
        // 没有稳定版时回退到最新版，保证“稳定版”也能安装，同时给出提示。
        _stableLoaderEntry = _hasStableLoader
            ? options.First(option => IsStableLoader(kind, option.Entry)).Entry
            : _latestLoaderEntry;
        CustomLoaderVersions.Clear();
        foreach (var option in options) CustomLoaderVersions.Add(option);
        SelectedCustomLoaderVersion = options.FirstOrDefault();
        LoaderStatus = options.Count == 0 ? "当前游戏版本没有可用的加载器版本" : string.Empty;
        UpdateLoaderVersionStatus();
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    private const string StableFallbackNotice = "该加载器当前没有稳定版，已选用最新版。";

    /// <summary>稳定版筛选中如果该加载器全是预览/测试版，给出明确提示。</summary>
    private void UpdateLoaderVersionStatus()
    {
        var showFallback = SelectedLoaderVersionFilter?.Kind == LoaderVersionFilterKind.Stable &&
                           _currentLoaderOptions is { Count: > 0 } && !_hasStableLoader;
        if (showFallback)
        {
            if (LoaderStatus != StableFallbackNotice)
                LoaderStatus = StableFallbackNotice;
        }
        else if (LoaderStatus == StableFallbackNotice)
        {
            LoaderStatus = string.Empty;
        }
    }

    private void ResetLoaderState()
    {
        _currentLoaderKind = null;
        _currentMcVersion = null;
        _currentLoaderOptions = null;
        _latestLoaderEntry = null;
        _stableLoaderEntry = null;
        CustomLoaderVersions.Clear();
        SelectedCustomLoaderVersion = null;
        LoaderStatus = string.Empty;
        IsLoaderVersionAreaVisible = false;
        IsCustomLoaderVersionsLoading = false;
    }

    private static bool IsStableLoader(LoaderKind kind, IInstallEntry entry) => kind switch
    {
        LoaderKind.Fabric => entry is FabricInstallEntry { Loader.IsStable: true },
        LoaderKind.Quilt => entry is QuiltInstallEntry { Loader.IsStable: true },
        LoaderKind.Forge or LoaderKind.NeoForge => entry is ForgeInstallEntry forge &&
            !forge.ForgeVersion.Contains("beta", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(forge.Branch) || !forge.Branch.Contains("beta", StringComparison.OrdinalIgnoreCase)),
        LoaderKind.OptiFine => entry is OptifineInstallEntry optifine &&
            !optifine.Patch.Contains("pre", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static string GetLoaderVersion(LoaderKind kind, IInstallEntry entry) =>
        MinecraftInstallationViewModel.GetLoaderVersion(kind, entry);

    private void UpdateRecommendedInstanceId()
    {
        Title = $"{(IsBedrockFilter ? "基岩" : "Java")} {SelectedVersion?.DisplayText}";
        if (IsBedrockFilter) return;
        // 未选中有效的游戏版本时不要改动实例 ID（用户可能正在输入版本关键字）。
        if (SelectedVersion?.Value is not VersionManifestEntry) return;
        var recommended = CreateRecommendedInstanceId();
        if (string.IsNullOrEmpty(InstanceId) || InstanceId == _lastRecommendedInstanceId)
            InstanceId = recommended;
        _lastRecommendedInstanceId = recommended;
    }

    private string CreateRecommendedInstanceId()
    {
        if (SelectedVersion?.Value is not VersionManifestEntry vanilla) return string.Empty;
        if (_currentLoaderKind is not { } kind || EffectiveLoaderEntry is not { } entry)
            return vanilla.Id;
        return $"{vanilla.Id} {kind}-{GetLoaderVersion(kind, entry)}";
    }

    private void UpdateVersionState()
    {
        ErrorText = Validate() ?? string.Empty;
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(IsVersionComboEnabled));
        OnPropertyChanged(nameof(IsCustomLoaderVersionComboEnabled));
        OnPropertyChanged(nameof(IsCustomLoaderVersionVisible));
    }

    private string? Validate()
    {
        if (SelectedVersion is null)
            return string.IsNullOrWhiteSpace(VersionSearchText)
                ? "请选择一个游戏版本"
                : $"未找到游戏版本“{VersionSearchText.Trim()}”，请从列表中选择一个有效版本";
        if (SelectedMinecraftFolder is null)
            return "请先在设置中添加一个标准游戏目录";
        if (SelectedVersion.Value is not VersionManifestEntry) return null;

        var id = InstanceId.Trim();
        if (string.IsNullOrWhiteSpace(id)) return "实例 ID 不能为空";
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "实例 ID 包含文件夹名称不允许的字符";
        if (Directory.Exists(Path.Combine(SelectedMinecraftFolder.FolderPath, "versions", id)))
            return "该实例 ID 已存在，请更换名称";
        return null;
    }

    private void CreateJava(VersionManifestEntry vanilla)
    {
        if (SelectedMinecraftFolder is not { } folder) return;

        var versionId = InstanceId.Trim();
        if (string.IsNullOrWhiteSpace(versionId)) versionId = vanilla.Id;
        var entries = new Dictionary<LoaderKind, IInstallEntry>();
        if (_currentLoaderKind is { } kind && EffectiveLoaderEntry is { } entry)
            entries[kind] = entry;
        var javaPath = MinecraftInstallationViewModel.GetJavaPath();
        Logger.Info($"[CreateInstance] Queuing Java installation {versionId} in {folder.FolderPath} with {entries.Count} loader(s).");
        var task = MinecraftInstallationViewModel.CreateInstallationTask(vanilla, folder, versionId, entries, javaPath);
        task.Start();
        if (_pendingIconData is not null)
            _ = ApplyJavaIconAfterInstallAsync(task, folder, versionId, _pendingIconData);
        RequestClose?.Invoke(this, true);
    }

    private void CreateBedrock(BedrockVersion version)
    {
        if (SelectedMinecraftFolder is not { } folder) return;

        var instanceName = version.BuildType == BedrockBuildType.UWP ? $"{version.Id}-UWP" : version.Id;
        Logger.Info($"[CreateInstance] Starting Bedrock installation {instanceName} in {folder.FolderPath}.");
        _ = new BedrockInstallationViewModel().InstallAsync(version, folder);
        if (_pendingIconData is not null)
            _ = ApplyBedrockIconAfterInstallAsync(folder, instanceName, _pendingIconData);
        RequestClose?.Invoke(this, true);
    }

    private static async Task ApplyJavaIconAfterInstallAsync(ManagedTask task, MinecraftFolderEntry folder,
        string versionId, byte[] iconData)
    {
        try
        {
            await task.Completion;
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[CreateInstance] Java icon application was cancelled for {versionId}: {exception}");
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning($"[CreateInstance] Java icon application failed for {versionId}: {exception}");
            return;
        }

        var instance = InstanceManager.Instance.Instances.FirstOrDefault(candidate =>
            candidate.IsJava &&
            string.Equals(candidate.MinecraftEntry?.Id, versionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.FolderPath, folder.FolderPath, StringComparison.OrdinalIgnoreCase));
        if (instance is not null) await ApplyIconAsync(instance, iconData);
    }

    private static async Task ApplyBedrockIconAfterInstallAsync(MinecraftFolderEntry folder, string instanceName,
        byte[] iconData)
    {
        var instance = await WaitForInstanceAsync(candidate =>
            candidate.IsBedrock &&
            string.Equals(candidate.BedrockConfig?.Name, instanceName, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromMinutes(6));
        if (instance is not null) await ApplyIconAsync(instance, iconData);
    }

    private static async Task<MinecraftInstance?> WaitForInstanceAsync(Func<MinecraftInstance, bool> match,
        TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            var found = InstanceManager.Instance.Instances.FirstOrDefault(match);
            if (found is not null) return found;
            await Task.Delay(500);
        }

        return null;
    }

    private static async Task ApplyIconAsync(MinecraftInstance instance, byte[] iconData)
    {
        try
        {
            using var stream = new MemoryStream(iconData);
            using var bitmap = new Bitmap(stream);
            instance.SetIcon(bitmap);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[CreateInstance] Failed to apply icon to {instance.InstanceName}: {exception}");
        }
    }
}
