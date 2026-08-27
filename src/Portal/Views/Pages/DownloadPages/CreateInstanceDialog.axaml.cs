using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
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

    private void ResetIcon_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as CreateInstanceDialogViewModel)?.ResetIcon();
    }

    private void Create_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as CreateInstanceDialogViewModel)?.Create();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as CreateInstanceDialogViewModel)?.Cancel();
    }
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
        or VersionFilterKind.JavaAprilFools or VersionFilterKind.JavaBeta or VersionFilterKind.JavaAlpha
        or VersionFilterKind.JavaUnobfuscated;
}

public enum VersionFilterKind
{
    JavaRelease,
    JavaSnapshot,
    JavaAprilFools,
    JavaBeta,
    JavaAlpha,
    JavaUnobfuscated,
    BedrockGdkRelease,
    BedrockGdkPreview,
    BedrockUwpRelease,
    BedrockUwpPreview
}

public sealed record VersionOption(string DisplayText, object Value)
{
    public override string ToString()
    {
        return DisplayText;
    }
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
    public override string ToString()
    {
        return DisplayText;
    }
}

public partial class CreateInstanceDialogViewModel : ObservableObject, IDialogContext, IDisposable
{
    private static readonly string StableFallbackNotice =
        CommonLanguageManager.Instance.createInstance_stableFallbackNotice.CurrentValue();
    private static readonly string DefaultIconResource = "Portal.Core.Assets.McIcons.01_grass_block_side.png";


    private static readonly IReadOnlyList<VersionFilterOption> JavaVersionFilters =
    [
        new(CommonLanguageManager.Instance.createInstance_versionRelease.CurrentValue(), VersionFilterKind.JavaRelease),
        new(CommonLanguageManager.Instance.createInstance_versionSnapshot.CurrentValue(), VersionFilterKind.JavaSnapshot),
        new(CommonLanguageManager.Instance.createInstance_versionAprilFools.CurrentValue(), VersionFilterKind.JavaAprilFools),
        new(CommonLanguageManager.Instance.createInstance_versionUnobfuscated.CurrentValue(), VersionFilterKind.JavaUnobfuscated),
        new(CommonLanguageManager.Instance.createInstance_versionBeta.CurrentValue(), VersionFilterKind.JavaBeta),
        new(CommonLanguageManager.Instance.createInstance_versionAlpha.CurrentValue(), VersionFilterKind.JavaAlpha)
    ];

    private static readonly IReadOnlyList<VersionFilterOption> BedrockVersionFilters = BuildBedrockVersionFilters();
    private readonly List<VersionOption> _categoryVersions = [];

    private readonly CancellationTokenSource _disposeCancellation = new();

    private readonly Dictionary<(string Version, LoaderKind Kind), IReadOnlyList<LoaderVersionOption>>
        _loaderOptionsCache = [];

    private readonly LoaderSelectionState _optifineState = new();
    private List<BedrockVersion> _bedrockVersions = [];
    private bool _bedrockVersionsLoaded;
    private string _currentMcVersion = string.Empty;
    private bool _disposed;
    private bool _isCreating;
    private bool _isSyncingVersionText;
    private List<MinecraftVersionListItem> _javaVersions = [];
    private bool _javaVersionsLoaded;
    private string _lastRecommendedInstanceId = string.Empty;
    private IconPickerResult? _pendingIcon;
    private byte[]? _pendingIconData;
    private LoaderSelectionState _primaryState = new();
    private bool _userTyping;
    private int _versionLoadGeneration;

    private bool _versionRefreshQueued;

    public CreateInstanceDialogViewModel()
    {
        MinecraftFolders = Data.ConfigEntry.InstallableMinecraftFolders.ToList();
        SelectedMinecraftFolder = Data.ConfigEntry.DefaultMinecraftFolder is { SupportsInstallation: true } folder &&
                                  MinecraftFolders.Contains(folder)
            ? folder
            : MinecraftFolders.FirstOrDefault();
        SelectedPlatform = Platforms[0];
        SelectedLoader = LoaderOptions[0];
        SelectedLoaderVersionFilter = LoaderVersionFilters[0];
        SelectedOptiFineVersionFilter = LoaderVersionFilters[0];
        UpdateLoaderIcon();
    }

    public ObservableCollection<VersionOption> Versions { get; } = [];
    public ObservableCollection<LoaderVersionOption> CustomLoaderVersions { get; } = [];
    public ObservableCollection<LoaderVersionOption> CustomOptiFineVersions { get; } = [];

    public IReadOnlyList<PlatformOption> Platforms { get; } =
    [
        new("Java", InstancePlatform.Java),
        new(CommonLanguageManager.Instance.createInstance_bedrock.CurrentValue(), InstancePlatform.Bedrock)
    ];

    public IReadOnlyList<VersionFilterOption> VersionFilters =>
        SelectedPlatform?.Platform == InstancePlatform.Bedrock ? BedrockVersionFilters : JavaVersionFilters;

    public IReadOnlyList<LoaderOption> LoaderOptions { get; } =
    [
        new(CommonLanguageManager.Instance.createInstance_noLoader.CurrentValue(), null),
        new("Fabric", LoaderKind.Fabric),
        new("NeoForge", LoaderKind.NeoForge),
        new("Forge", LoaderKind.Forge),
        new("Quilt", LoaderKind.Quilt)
    ];

    public IReadOnlyList<LoaderVersionFilterOption> LoaderVersionFilters { get; } =
    [
        new(CommonLanguageManager.Instance.createInstance_loaderStable.CurrentValue(), LoaderVersionFilterKind.Stable),
        new(CommonLanguageManager.Instance.createInstance_loaderLatest.CurrentValue(), LoaderVersionFilterKind.Latest),
        new(CommonLanguageManager.Instance.createInstance_loaderOther.CurrentValue(), LoaderVersionFilterKind.Other)
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
    [ObservableProperty] public partial string VersionsPlaceholder { get; set; } =
        CommonLanguageManager.Instance.common_loading.CurrentValue();
    [ObservableProperty] public partial bool IsLoaderVisible { get; set; }
    [ObservableProperty] public partial LoaderOption? SelectedLoader { get; set; }
    [ObservableProperty] public partial LoaderVersionFilterOption? SelectedLoaderVersionFilter { get; set; }
    [ObservableProperty] public partial LoaderVersionOption? SelectedCustomLoaderVersion { get; set; }
    [ObservableProperty] public partial bool IsLoaderVersionAreaVisible { get; set; }
    [ObservableProperty] public partial bool IsCustomLoaderVersionsLoading { get; set; }
    [ObservableProperty] public partial string CustomLoaderVersionsPlaceholder { get; set; } =
        CommonLanguageManager.Instance.common_loading.CurrentValue();
    [ObservableProperty] public partial string LoaderStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsOptiFineSelected { get; set; }
    [ObservableProperty] public partial LoaderVersionFilterOption? SelectedOptiFineVersionFilter { get; set; }
    [ObservableProperty] public partial LoaderVersionOption? SelectedCustomOptiFineVersion { get; set; }
    [ObservableProperty] public partial bool IsOptiFineLoaderVersionAreaVisible { get; set; }
    [ObservableProperty] public partial bool IsCustomOptiFineVersionsLoading { get; set; }
    [ObservableProperty] public partial string CustomOptiFineVersionsPlaceholder { get; set; } =
        CommonLanguageManager.Instance.common_loading.CurrentValue();
    [ObservableProperty] public partial string OptiFineStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial string ErrorText { get; set; } = string.Empty;
    [ObservableProperty] public partial Bitmap? IconPreview { get; set; }

    public bool IsVersionComboEnabled => !IsVersionsLoading;
    public bool IsCustomLoaderVersionComboEnabled => !IsCustomLoaderVersionsLoading && CustomLoaderVersions.Count > 0;

    public bool IsCustomLoaderVersionVisible =>
        SelectedLoader?.Kind is not null && SelectedLoaderVersionFilter?.Kind == LoaderVersionFilterKind.Other;

    public bool IsCustomOptiFineVersionVisible =>
        IsOptiFineSelected && SelectedOptiFineVersionFilter?.Kind == LoaderVersionFilterKind.Other;

    public bool IsCustomOptiFineVersionComboEnabled =>
        !IsCustomOptiFineVersionsLoading && CustomOptiFineVersions.Count > 0;

    public bool IsOptiFineToggleVisible => SelectedLoader?.Kind is null or LoaderKind.Forge;
    public bool HasLoaderStatus => !string.IsNullOrEmpty(LoaderStatus);
    public bool HasOptiFineStatus => !string.IsNullOrEmpty(OptiFineStatus);
    public bool HasErrorText => !string.IsNullOrEmpty(ErrorText);

    public bool CanCreate => !_isCreating && SelectedVersion is not null && !IsVersionsLoading &&
                             SelectedLoadersReady() &&
                             string.IsNullOrEmpty(ErrorText);

    private bool IsBedrockFilter => SelectedPlatform?.Platform == InstancePlatform.Bedrock;

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    public void Dispose()
    {
        if (_disposed) return;
        Logger.Info("[CreateInstance] Dialog disposed; cancelling pending version and loader requests.");
        _disposed = true;
        _disposeCancellation.Cancel();
    }

    private bool SelectedLoadersReady()
    {
        if (_primaryState.Kind is not null)
        {
            if (IsCustomLoaderVersionsLoading) return false;
            if (ResolveEffectiveEntry(_primaryState, SelectedLoaderVersionFilter?.Kind) is null) return false;
        }

        if (IsOptiFineSelected && _optifineState.Kind is not null)
        {
            if (IsCustomOptiFineVersionsLoading) return false;
            if (ResolveEffectiveEntry(_optifineState, SelectedOptiFineVersionFilter?.Kind) is null) return false;
        }

        return true;
    }

    private Dictionary<LoaderKind, IInstallEntry> EffectiveLoaderEntries()
    {
        var result = new Dictionary<LoaderKind, IInstallEntry>();
        if (_primaryState.Kind is { } primaryKind)
        {
            var entry = ResolveEffectiveEntry(_primaryState, SelectedLoaderVersionFilter?.Kind);
            if (entry is not null) result[primaryKind] = entry;
        }

        if (IsOptiFineSelected && _optifineState.Kind is { } optifineKind)
        {
            var entry = ResolveEffectiveEntry(_optifineState, SelectedOptiFineVersionFilter?.Kind);
            if (entry is not null) result[optifineKind] = entry;
        }

        return result;
    }

    private static IInstallEntry? ResolveEffectiveEntry(LoaderSelectionState state, LoaderVersionFilterKind? filter)
    {
        return filter switch
        {
            LoaderVersionFilterKind.Stable => state.StableEntry,
            LoaderVersionFilterKind.Latest => state.LatestEntry,
            LoaderVersionFilterKind.Other => state.CustomVersion?.Entry,
            _ => null
        };
    }

    partial void OnSelectedPlatformChanged(PlatformOption? value)
    {
        OnPropertyChanged(nameof(VersionFilters));
        IsLoaderVisible = value?.Platform == InstancePlatform.Java;
        CanCustomizeInstanceId = value?.Platform == InstancePlatform.Java;
        if (IsLoaderVisible)
        {
            SyncPrimaryLoaderState();
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
        if (IsVersionDropDownOpen && !_isSyncingVersionText)
            _userTyping = true;
        QueueVersionRefresh();
    }

    partial void OnInstanceIdChanged(string value)
    {
        UpdateVersionState();
    }

    partial void OnIsVersionDropDownOpenChanged(bool value)
    {
        if (!value)
            _userTyping = false;
        QueueVersionRefresh();
        UpdateVersionState();
    }

    public void NotifyVersionTextInput()
    {
        _userTyping = true;
    }

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

        if (value?.Value is VersionManifestEntry vanilla)
        {
            _currentMcVersion = vanilla.Id;
            if (_primaryState.Kind is { } kind)
                _ = EnsurePrimaryLoaderVersionsAsync(kind, vanilla.Id);
            if (IsOptiFineSelected)
                _ = EnsureOptifineLoaderVersionsAsync(LoaderKind.OptiFine, vanilla.Id);
        }

        UpdateLoaderIcon();
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedLoaderChanged(LoaderOption? value)
    {
        SyncPrimaryLoaderState();
        UpdateLoaderIcon();
    }

    private void SyncPrimaryLoaderState()
    {
        var kind = SelectedLoader?.Kind;
        if (kind != _primaryState.Kind)
        {
            var incompatibleOptiFine = IsOptiFineSelected && kind is not null && kind != LoaderKind.Forge;
            _primaryState = new LoaderSelectionState { Kind = kind, McVersion = _currentMcVersion };

            if (incompatibleOptiFine)
                IsOptiFineSelected = false;
            SelectedCustomLoaderVersion = null;
            CustomLoaderVersions.Clear();
            IsCustomLoaderVersionsLoading = false;
            LoaderStatus = string.Empty;
        }

        IsLoaderVersionAreaVisible = kind is not null;
        if (kind is { } loaderKind)
        {
            if (SelectedVersion?.Value is VersionManifestEntry vanilla)
                _ = EnsurePrimaryLoaderVersionsAsync(loaderKind, vanilla.Id);
            else
                LoaderStatus = CommonLanguageManager.Instance.createInstance_selectGameVersionFirst.CurrentValue();
        }

        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedLoaderVersionFilterChanged(LoaderVersionFilterOption? value)
    {
        if (value?.Kind == LoaderVersionFilterKind.Other && _primaryState.Kind is { } kind &&
            _primaryState.McVersion is { Length: > 0 } version)
            _ = EnsurePrimaryLoaderVersionsAsync(kind, version);
        UpdateLoaderVersionStatus();
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedOptiFineVersionFilterChanged(LoaderVersionFilterOption? value)
    {
        if (value?.Kind == LoaderVersionFilterKind.Other && _optifineState.Kind is { } kind &&
            _optifineState.McVersion is { Length: > 0 } version)
            _ = EnsureOptifineLoaderVersionsAsync(kind, version);
        UpdateOptiFineVersionStatus();
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedCustomLoaderVersionChanged(LoaderVersionOption? value)
    {
        _primaryState.CustomVersion = value;
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedCustomOptiFineVersionChanged(LoaderVersionOption? value)
    {
        _optifineState.CustomVersion = value;
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnIsOptiFineSelectedChanged(bool value)
    {
        if (value)
        {
            _optifineState.Kind = LoaderKind.OptiFine;
            IsOptiFineLoaderVersionAreaVisible = true;
            if (SelectedVersion?.Value is VersionManifestEntry vanilla)
                _ = EnsureOptifineLoaderVersionsAsync(LoaderKind.OptiFine, vanilla.Id);
            else
                OptiFineStatus = CommonLanguageManager.Instance.createInstance_selectGameVersionFirst.CurrentValue();
        }
        else
        {
            _optifineState.LoadGeneration++;
            _optifineState.Kind = null;
            _optifineState.Options = [];
            _optifineState.LatestEntry = null;
            _optifineState.StableEntry = null;
            _optifineState.HasStable = false;
            _optifineState.CustomVersion = null;
            IsOptiFineLoaderVersionAreaVisible = false;
            IsCustomOptiFineVersionsLoading = false;
            SelectedCustomOptiFineVersion = null;
            CustomOptiFineVersions.Clear();
            OptiFineStatus = string.Empty;
        }

        UpdateLoaderIcon();
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    partial void OnSelectedMinecraftFolderChanged(MinecraftFolderEntry? value)
    {
        UpdateVersionState();
    }

    partial void OnIsVersionsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVersionComboEnabled));
    }

    partial void OnIsCustomLoaderVersionsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCustomLoaderVersionComboEnabled));
    }

    partial void OnIsCustomOptiFineVersionsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCustomOptiFineVersionComboEnabled));
    }

    partial void OnErrorTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasErrorText));
    }

    partial void OnLoaderStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasLoaderStatus));
    }

    partial void OnOptiFineStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasOptiFineStatus));
    }

    public async Task SetPendingIconAsync(IconPickerResult result)
    {
        _pendingIcon = result;
        try
        {
            using var stream = result.CustomImageFile is not null
                ? await result.CustomImageFile.OpenReadAsync()
                : typeof(MinecraftInstance).Assembly.GetManifestResourceStream(result.BuiltInResourceName!);
            if (stream is null) return;


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

    private string GetSuggestedIconResource()
    {
        if (IsBedrockFilter)
            return DefaultIconResource;

        if (SelectedLoader?.Kind is { } kind)
            return kind switch
            {
                LoaderKind.Fabric => "Portal.Core.Assets.McIcons.05_FabricIcon.png",
                LoaderKind.Forge => "Portal.Core.Assets.McIcons.06_ForgeIcon.png",
                LoaderKind.NeoForge => "Portal.Core.Assets.McIcons.07_NeoForgeIcon.png",
                LoaderKind.Quilt => "Portal.Core.Assets.McIcons.09_QuiltIcon.png",
                _ => DefaultIconResource
            };

        if (IsOptiFineSelected)
            return "Portal.Core.Assets.McIcons.08_OptiFineIcon.png";

        if (SelectedVersion?.Value is VersionManifestEntry vanilla &&
            string.Equals(vanilla.Type, "snapshot", StringComparison.OrdinalIgnoreCase))
            return "Portal.Core.Assets.McIcons.02_crafting_table_front.png";

        return DefaultIconResource;
    }

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
        Logger.Info(
            $"[CreateInstance] Creating {SelectedPlatform?.Platform} instance {InstanceId.Trim()} in {SelectedMinecraftFolder?.FolderPath} from version {SelectedVersion.DisplayText}.");

        if (selected is VersionManifestEntry vanilla)
            CreateJava(vanilla);
        else if (selected is BedrockVersion bedrock)
            CreateBedrock(bedrock);
    }

    public void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }

    private static IReadOnlyList<VersionFilterOption> BuildBedrockVersionFilters()
    {
        var filters = new List<VersionFilterOption>
        {
            new(CommonLanguageManager.Instance.createInstance_gdkRelease.CurrentValue(), VersionFilterKind.BedrockGdkRelease),
            new(CommonLanguageManager.Instance.createInstance_gdkPreview.CurrentValue(), VersionFilterKind.BedrockGdkPreview)
        };
        if (OperatingSystem.IsWindows())
        {
            filters.Add(new VersionFilterOption(CommonLanguageManager.Instance.createInstance_uwpRelease.CurrentValue(),
                VersionFilterKind.BedrockUwpRelease));
            filters.Add(new VersionFilterOption(CommonLanguageManager.Instance.createInstance_uwpPreview.CurrentValue(),
                VersionFilterKind.BedrockUwpPreview));
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
        VersionsPlaceholder = CommonLanguageManager.Instance.common_loading.CurrentValue();
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
            Logger.Info(
                $"[CreateInstance] Loaded {_categoryVersions.Count} {filter.Kind} version(s) in {stopwatch.Elapsed}.");
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            Logger.Debug(
                $"[CreateInstance] Loading {filter.Kind} version list was cancelled after {stopwatch.Elapsed}.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (generation != _versionLoadGeneration || _disposed) return;
            IsVersionsLoading = false;
            VersionsPlaceholder = CommonLanguageManager.Instance.createInstance_fetchVersionsFailed.CurrentValue();
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
            {
                entries.AddRange(loaded);
                UnlistedVersions.MergeInto(entries);
            }
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
                    VersionFilterKind.JavaSnapshot => version.RawType is "snapshot" or "pending",
                    VersionFilterKind.JavaAprilFools => MinecraftVersionListItem.IsAprilFoolsVersion(version.Name),
                    VersionFilterKind.JavaBeta => version.RawType == "old_beta",
                    VersionFilterKind.JavaAlpha => version.RawType == "old_alpha",
                    VersionFilterKind.JavaUnobfuscated => version.RawType == "unobfuscated",
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


        var isFiltering = IsVersionDropDownOpen && _userTyping && query.Length > 0;


        var keep = new HashSet<VersionOption>(_categoryVersions.Where(version =>
            !isFiltering || version.DisplayText.Contains(query, StringComparison.OrdinalIgnoreCase)));
        if (selected is not null) keep.Add(selected);

        for (var i = Versions.Count - 1; i >= 0; i--)
            if (!keep.Contains(Versions[i]))
                Versions.RemoveAt(i);

        foreach (var item in keep)
            if (!Versions.Contains(item))
                Versions.Add(item);

        VersionsPlaceholder = IsVersionsLoading
            ? CommonLanguageManager.Instance.common_loading.CurrentValue()
            : _categoryVersions.Count == 0
                ? CommonLanguageManager.Instance.createInstance_noVersions.CurrentValue()
                : Versions.Count == 0
                    ? CommonLanguageManager.Instance.createInstance_noMatchVersions.CurrentValue()
                    : CommonLanguageManager.Instance.createInstance_selectVersion.CurrentValue();

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

    private async Task EnsurePrimaryLoaderVersionsAsync(LoaderKind kind, string mcVersion)
    {
        var generation = ++_primaryState.LoadGeneration;
        _primaryState.Kind = kind;
        _primaryState.McVersion = mcVersion;

        if (_loaderOptionsCache.TryGetValue((mcVersion, kind), out var cached))
        {
            _primaryState.Options = cached;
            ApplyPrimaryLoaderOptions();
            return;
        }


        ClearPrimaryLoaderState();
        IsCustomLoaderVersionsLoading = true;
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[CreateInstance] Loading {kind} versions for Minecraft {mcVersion}.");
        CustomLoaderVersionsPlaceholder = CommonLanguageManager.Instance.common_loading.CurrentValue();
        UpdateVersionState();
        try
        {
            var entries = await FetchLoaderVersionsAsync(kind, mcVersion);
            var options = entries.Select(entry => new LoaderVersionOption(GetLoaderVersion(kind, entry), entry))
                .ToList();
            _loaderOptionsCache[(mcVersion, kind)] = options;
            if (generation != _primaryState.LoadGeneration || _disposed) return;
            _primaryState.Options = options;
            ApplyPrimaryLoaderOptions();
            Logger.Info(
                $"[CreateInstance] Loaded {options.Count} {kind} version(s) for Minecraft {mcVersion} in {stopwatch.Elapsed}.");
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            Logger.Debug(
                $"[CreateInstance] Loading {kind} versions for Minecraft {mcVersion} was cancelled after {stopwatch.Elapsed}.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (generation != _primaryState.LoadGeneration || _disposed) return;
            LoaderStatus = CommonLanguageManager.Instance.createInstance_fetchLoaderVersionsFailed.CurrentValue();
        }
        finally
        {
            if (generation == _primaryState.LoadGeneration)
            {
                IsCustomLoaderVersionsLoading = false;
                UpdateVersionState();
            }
        }
    }

    private async Task EnsureOptifineLoaderVersionsAsync(LoaderKind kind, string mcVersion)
    {
        var generation = ++_optifineState.LoadGeneration;
        _optifineState.Kind = kind;
        _optifineState.McVersion = mcVersion;

        if (_loaderOptionsCache.TryGetValue((mcVersion, kind), out var cached))
        {
            _optifineState.Options = cached;
            ApplyOptiFineOptions();
            return;
        }

        ClearOptiFineState();
        IsCustomOptiFineVersionsLoading = true;
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[CreateInstance] Loading {kind} versions for Minecraft {mcVersion}.");
        CustomOptiFineVersionsPlaceholder = CommonLanguageManager.Instance.common_loading.CurrentValue();
        UpdateVersionState();
        try
        {
            var entries = await FetchLoaderVersionsAsync(kind, mcVersion);
            var options = entries.Select(entry => new LoaderVersionOption(GetLoaderVersion(kind, entry), entry))
                .ToList();
            _loaderOptionsCache[(mcVersion, kind)] = options;
            if (generation != _optifineState.LoadGeneration || _disposed) return;
            _optifineState.Options = options;
            ApplyOptiFineOptions();
            Logger.Info(
                $"[CreateInstance] Loaded {options.Count} {kind} version(s) for Minecraft {mcVersion} in {stopwatch.Elapsed}.");
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            Logger.Debug(
                $"[CreateInstance] Loading {kind} versions for Minecraft {mcVersion} was cancelled after {stopwatch.Elapsed}.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (generation != _optifineState.LoadGeneration || _disposed) return;
            OptiFineStatus = CommonLanguageManager.Instance.createInstance_fetchOptifineVersionsFailed.CurrentValue();
        }
        finally
        {
            if (generation == _optifineState.LoadGeneration)
            {
                IsCustomOptiFineVersionsLoading = false;
                UpdateVersionState();
            }
        }
    }

    private static async Task<IReadOnlyList<IInstallEntry>> FetchLoaderVersionsAsync(LoaderKind kind, string mcVersion)
    {
        return kind switch
        {
            LoaderKind.Fabric => (await FabricInstaller.EnumerableFabricAsync(mcVersion)).Cast<IInstallEntry>()
                .ToList(),
            LoaderKind.Forge => (await ForgeInstaller.EnumerableForgeAsync(mcVersion)).Cast<IInstallEntry>().ToList(),
            LoaderKind.NeoForge => (await ForgeInstaller.EnumerableForgeAsync(mcVersion, true)).Cast<IInstallEntry>()
                .ToList(),
            LoaderKind.Quilt => (await QuiltInstaller.EnumerableQuiltAsync(mcVersion)).Cast<IInstallEntry>().ToList(),
            LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(mcVersion)).Cast<IInstallEntry>()
                .ToList(),
            _ => []
        };
    }

    private void ApplyPrimaryLoaderOptions()
    {
        var state = _primaryState;
        state.LatestEntry = state.Options.FirstOrDefault()?.Entry;
        state.HasStable = state.Kind is { } kind && state.Options.Any(option => IsStableLoader(kind, option.Entry));

        state.StableEntry = state.HasStable
            ? state.Options.First(option => IsStableLoader(state.Kind!.Value, option.Entry)).Entry
            : state.LatestEntry;
        CustomLoaderVersions.Clear();
        foreach (var option in state.Options) CustomLoaderVersions.Add(option);

        if (state.CustomVersion is { } custom && state.Options.Contains(custom))
            SelectedCustomLoaderVersion = custom;
        else
            SelectedCustomLoaderVersion = state.Options.FirstOrDefault();
        state.CustomVersion = SelectedCustomLoaderVersion;
        LoaderStatus = state.Options.Count == 0
            ? CommonLanguageManager.Instance.createInstance_noLoaderVersions.CurrentValue()
            : string.Empty;
        UpdateLoaderVersionStatus();
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    private void ApplyOptiFineOptions()
    {
        var state = _optifineState;
        state.LatestEntry = state.Options.FirstOrDefault()?.Entry;
        state.HasStable = state.Kind is { } kind && state.Options.Any(option => IsStableLoader(kind, option.Entry));

        state.StableEntry = state.HasStable
            ? state.Options.First(option => IsStableLoader(state.Kind!.Value, option.Entry)).Entry
            : state.LatestEntry;
        CustomOptiFineVersions.Clear();
        foreach (var option in state.Options) CustomOptiFineVersions.Add(option);

        if (state.CustomVersion is { } custom && state.Options.Contains(custom))
            SelectedCustomOptiFineVersion = custom;
        else
            SelectedCustomOptiFineVersion = state.Options.FirstOrDefault();
        state.CustomVersion = SelectedCustomOptiFineVersion;
        OptiFineStatus = state.Options.Count == 0
            ? CommonLanguageManager.Instance.createInstance_noOptifineVersions.CurrentValue()
            : string.Empty;
        UpdateOptiFineVersionStatus();
        UpdateRecommendedInstanceId();
        UpdateVersionState();
    }

    private void ClearPrimaryLoaderState()
    {
        _primaryState.Options = [];
        _primaryState.LatestEntry = null;
        _primaryState.StableEntry = null;
        _primaryState.HasStable = false;
        _primaryState.CustomVersion = null;
        SelectedCustomLoaderVersion = null;
        CustomLoaderVersions.Clear();
    }

    private void ClearOptiFineState()
    {
        _optifineState.Options = [];
        _optifineState.LatestEntry = null;
        _optifineState.StableEntry = null;
        _optifineState.HasStable = false;
        _optifineState.CustomVersion = null;
        SelectedCustomOptiFineVersion = null;
        CustomOptiFineVersions.Clear();
    }

    private void UpdateLoaderVersionStatus()
    {
        var showFallback = SelectedLoaderVersionFilter?.Kind == LoaderVersionFilterKind.Stable &&
                           _primaryState.Options.Count > 0 && !_primaryState.HasStable;
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

    private void UpdateOptiFineVersionStatus()
    {
        var showFallback = SelectedOptiFineVersionFilter?.Kind == LoaderVersionFilterKind.Stable &&
                           _optifineState.Options.Count > 0 && !_optifineState.HasStable;
        if (showFallback)
        {
            if (OptiFineStatus != StableFallbackNotice)
                OptiFineStatus = StableFallbackNotice;
        }
        else if (OptiFineStatus == StableFallbackNotice)
        {
            OptiFineStatus = string.Empty;
        }
    }

    private void ResetLoaderState()
    {
        _primaryState = new LoaderSelectionState();

        _optifineState.LoadGeneration++;
        IsOptiFineSelected = false;
        IsLoaderVersionAreaVisible = false;
        IsOptiFineLoaderVersionAreaVisible = false;
        SelectedCustomLoaderVersion = null;
        CustomLoaderVersions.Clear();
        IsCustomLoaderVersionsLoading = false;
        IsCustomOptiFineVersionsLoading = false;
        SelectedCustomOptiFineVersion = null;
        CustomOptiFineVersions.Clear();
        LoaderStatus = string.Empty;
        OptiFineStatus = string.Empty;
    }

    private static bool IsStableLoader(LoaderKind kind, IInstallEntry entry)
    {
        return kind switch
        {
            LoaderKind.Fabric => entry is FabricInstallEntry { Loader.IsStable: true },
            LoaderKind.Quilt => entry is QuiltInstallEntry { Loader.IsStable: true },
            LoaderKind.Forge or LoaderKind.NeoForge => entry is ForgeInstallEntry forge &&
                                                       !forge.ForgeVersion.Contains("beta",
                                                           StringComparison.OrdinalIgnoreCase) &&
                                                       (string.IsNullOrEmpty(forge.Branch) ||
                                                        !forge.Branch.Contains("beta",
                                                            StringComparison.OrdinalIgnoreCase)),
            LoaderKind.OptiFine => entry is OptifineInstallEntry optifine &&
                                   !optifine.Patch.Contains("pre", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string GetLoaderVersion(LoaderKind kind, IInstallEntry entry)
    {
        return MinecraftInstallationViewModel.GetLoaderVersion(kind, entry);
    }

    private void UpdateRecommendedInstanceId()
    {
        Title = string.Format(CommonLanguageManager.Instance.createInstance_titleFormat.CurrentValue(),
            IsBedrockFilter ? CommonLanguageManager.Instance.createInstance_bedrock.CurrentValue() : "Java",
            SelectedVersion?.DisplayText);
        if (IsBedrockFilter) return;

        if (SelectedVersion?.Value is not VersionManifestEntry) return;
        var recommended = CreateRecommendedInstanceId();
        if (string.IsNullOrEmpty(InstanceId) || InstanceId == _lastRecommendedInstanceId)
            InstanceId = recommended;
        _lastRecommendedInstanceId = recommended;
    }

    private string CreateRecommendedInstanceId()
    {
        if (SelectedVersion?.Value is not VersionManifestEntry vanilla) return string.Empty;
        var entries = EffectiveLoaderEntries();
        if (entries.Count == 0) return vanilla.Id;
        var names = entries.Select(pair => $"{pair.Key}-{GetLoaderVersion(pair.Key, pair.Value)}");
        return $"{vanilla.Id} {string.Join(" + ", names)}";
    }

    private void UpdateVersionState()
    {
        ErrorText = Validate() ?? string.Empty;
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(IsVersionComboEnabled));
        OnPropertyChanged(nameof(IsCustomLoaderVersionComboEnabled));
        OnPropertyChanged(nameof(IsCustomLoaderVersionVisible));
        OnPropertyChanged(nameof(IsCustomOptiFineVersionComboEnabled));
        OnPropertyChanged(nameof(IsCustomOptiFineVersionVisible));
        OnPropertyChanged(nameof(IsOptiFineToggleVisible));
    }

    private string? Validate()
    {
        if (SelectedVersion is null)
            return string.IsNullOrWhiteSpace(VersionSearchText)
                ? CommonLanguageManager.Instance.createInstance_selectVersion.CurrentValue()
                : string.Format(
                    CommonLanguageManager.Instance.createInstance_versionNotFound.CurrentValue(),
                    VersionSearchText.Trim());
        if (SelectedMinecraftFolder is null)
            return CommonLanguageManager.Instance.createInstance_addStandardFolder.CurrentValue();
        if (SelectedVersion.Value is not VersionManifestEntry) return null;

        var id = InstanceId.Trim();
        if (string.IsNullOrWhiteSpace(id)) return CommonLanguageManager.Instance.createInstance_idEmpty.CurrentValue();
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return CommonLanguageManager.Instance.createInstance_idInvalidChars.CurrentValue();
        var instanceDirectory = SelectedMinecraftFolder.DetectedLayout.Kind == MinecraftFolderKind.PortalMc
            ? Path.Combine(SelectedMinecraftFolder.FolderPath, "instances", id)
            : Path.Combine(SelectedMinecraftFolder.FolderPath, "versions", id);
        if (Directory.Exists(instanceDirectory))
            return CommonLanguageManager.Instance.createInstance_idExists.CurrentValue();
        return null;
    }

    private void CreateJava(VersionManifestEntry vanilla)
    {
        if (SelectedMinecraftFolder is not { } folder) return;

        var versionId = InstanceId.Trim();
        if (string.IsNullOrWhiteSpace(versionId)) versionId = vanilla.Id;
        var entries = EffectiveLoaderEntries();
        var javaPath = MinecraftInstallationViewModel.GetJavaPath(vanilla.Id);
        Logger.Info(
            $"[CreateInstance] Queuing Java installation {versionId} in {folder.FolderPath} with {entries.Count} loader(s).");
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

    private sealed class LoaderSelectionState
    {
        public LoaderVersionOption? CustomVersion;
        public bool HasStable;
        public LoaderKind? Kind;
        public IInstallEntry? LatestEntry;
        public int LoadGeneration;
        public string McVersion = string.Empty;
        public IReadOnlyList<LoaderVersionOption> Options = [];
        public IInstallEntry? StableEntry;
    }
}
