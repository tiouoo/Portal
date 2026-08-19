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
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

internal partial class VersionModifyDialog : UserControl
{
    public VersionModifyDialog(MinecraftInstance instance)
    {
        InitializeComponent();
        DataContext = new VersionModifyDialogViewModel(instance);
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
        if (DataContext is VersionModifyDialogViewModel viewModel)
            viewModel.NotifyVersionTextInput();
    }

    private async void SelectLoaderVersion_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VersionModifyDialogViewModel viewModel)
            await viewModel.SelectLoaderVersionAsync(this);
    }

    private void UninstallAllLoaders_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VersionModifyDialogViewModel viewModel)
            viewModel.UninstallAllLoaders();
    }

    private void Modify_OnClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = (VersionModifyDialogViewModel)DataContext!;
        _ = viewModel.ModifyAsync();
        viewModel.Complete();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as VersionModifyDialogViewModel)?.Cancel();
    }
}

public sealed record VersionModifyDialogResult;

public partial class VersionModifyDialogViewModel : ObservableObject, IDialogContext, IDisposable
{
    private readonly Dictionary<LoaderKind, IReadOnlyList<IInstallEntry>> _availableLoaderVersions = [];
    private readonly List<VersionOption> _categoryVersions = [];
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Dictionary<LoaderKind, string> _installedLoaderVersions = [];
    private readonly MinecraftInstance _instance;
    private readonly Dictionary<LoaderKind, int> _loadGenerations = [];

    private readonly Dictionary<(string Version, LoaderKind Kind), IReadOnlyList<IInstallEntry>> _loaderOptionsCache =
        [];

    private readonly Dictionary<LoaderKind, IInstallEntry> _selectedLoaders = [];
    private bool _disposed;
    private bool _isModifying;
    private bool _isSyncingVersionText;
    private List<MinecraftVersionListItem> _javaVersions = [];
    private int _loadingCount;
    private bool _updatingSelection;
    private bool _userTyping;
    private VersionManifestEntry? _vanilla;
    private int _versionLoadGeneration;
    private bool _versionRefreshQueued;

    public VersionModifyDialogViewModel(MinecraftInstance instance)
    {
        _instance = instance;
        SelectedVersionFilter = VersionFilters[0];
        PreselectInstalledLoaders();
        DependentWarning = BuildDependentWarning(instance);
        UpdateVersionState();
    }

    public string InstanceName => _instance.InstanceName;

    public string CurrentVersionText
    {
        get
        {
            var entry = _instance.MinecraftEntry;
            if (entry is null) return string.Empty;
            var current = entry.Version.VersionId;
            return entry is ModifiedMinecraftEntry { InheritedMinecraft: { } inherited }
                ? string.Format(CommonLanguageManager.Instance.minecraftInstall_currentVersionWithBase.CurrentValue(),
                    current, inherited.Version.VersionId)
                : string.Format(CommonLanguageManager.Instance.minecraftInstall_currentVersion.CurrentValue(), current);
        }
    }

    public ObservableCollection<VersionOption> Versions { get; } = [];

    public IReadOnlyList<VersionFilterOption> VersionFilters { get; } =
    [
        new(CommonLanguageManager.Instance.createInstance_versionRelease.CurrentValue(), VersionFilterKind.JavaRelease),
        new(CommonLanguageManager.Instance.createInstance_versionSnapshot.CurrentValue(), VersionFilterKind.JavaSnapshot),
        new(CommonLanguageManager.Instance.createInstance_versionAprilFools.CurrentValue(), VersionFilterKind.JavaAprilFools),
        new(CommonLanguageManager.Instance.createInstance_versionUnobfuscated.CurrentValue(), VersionFilterKind.JavaUnobfuscated),
        new(CommonLanguageManager.Instance.createInstance_versionBeta.CurrentValue(), VersionFilterKind.JavaBeta),
        new(CommonLanguageManager.Instance.createInstance_versionAlpha.CurrentValue(), VersionFilterKind.JavaAlpha)
    ];

    [ObservableProperty] public partial VersionFilterOption? SelectedVersionFilter { get; set; }
    [ObservableProperty] public partial string VersionSearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsVersionDropDownOpen { get; set; }
    [ObservableProperty] public partial VersionOption? SelectedVersion { get; set; }
    [ObservableProperty] public partial bool IsVersionsLoading { get; set; }

    [ObservableProperty] public partial string VersionsPlaceholder { get; set; } =
        CommonLanguageManager.Instance.common_loading.CurrentValue();
    [ObservableProperty] public partial bool IsFabricSelected { get; set; }
    [ObservableProperty] public partial bool IsForgeSelected { get; set; }
    [ObservableProperty] public partial bool IsNeoForgeSelected { get; set; }
    [ObservableProperty] public partial bool IsQuiltSelected { get; set; }
    [ObservableProperty] public partial bool IsOptiFineSelected { get; set; }
    [ObservableProperty] public partial string FabricStatus { get; set; } =
        CommonLanguageManager.Instance.minecraftInstall_noLoader.CurrentValue();
    [ObservableProperty] public partial string ForgeStatus { get; set; } =
        CommonLanguageManager.Instance.minecraftInstall_noLoader.CurrentValue();
    [ObservableProperty] public partial string NeoForgeStatus { get; set; } =
        CommonLanguageManager.Instance.minecraftInstall_noLoader.CurrentValue();
    [ObservableProperty] public partial string QuiltStatus { get; set; } =
        CommonLanguageManager.Instance.minecraftInstall_noLoader.CurrentValue();
    [ObservableProperty] public partial string OptiFineStatus { get; set; } =
        CommonLanguageManager.Instance.minecraftInstall_noLoader.CurrentValue();

    public bool HasModLoader => IsFabricSelected || IsForgeSelected || IsNeoForgeSelected || IsQuiltSelected ||
                                IsOptiFineSelected;

    public bool RequiresJava => IsForgeSelected || IsNeoForgeSelected || IsOptiFineSelected;
    public bool IsVersionComboEnabled => !IsVersionsLoading;
    public bool CanSelectLoaderVersion => HasModLoader && _loadingCount == 0 && SelectedLoadersAreReady();

    public bool CanModify => !_isModifying && _loadingCount == 0 && !IsVersionsLoading &&
                             SelectedVersion?.Value is VersionManifestEntry && SelectedLoadersAreReady() &&
                             HasChanges && !HasDependentWarning;

    public bool CanUninstallAllLoaders =>
        _instance.MinecraftEntry is ModifiedMinecraftEntry { ModLoaders: { } loaders } && loaders.Any();

    [ObservableProperty] public partial string ModifyPreviewText { get; set; } = string.Empty;

    public bool HasChanges { get; private set; }

    public string DependentWarning { get; }
    public bool HasDependentWarning => DependentWarning.Length > 0;

    public ManagedTask? StartedTask { get; private set; }

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCancellation.Cancel();
    }

    partial void OnIsVersionsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVersionComboEnabled));
    }

    private static string BuildDependentWarning(MinecraftInstance instance)
    {
        if (instance.MinecraftEntry is not { } entry)
            return string.Empty;

        var dependents = VersionModifyService.FindDependentVersionIds(entry.MinecraftFolderPath, entry.Id);
        return dependents.Count == 0
            ? string.Empty
            : string.Format(CommonLanguageManager.Instance.minecraftInstall_dependentWarning.CurrentValue(),
                string.Join("、", dependents));
    }

    private void PreselectInstalledLoaders()
    {
        if (_instance.MinecraftEntry is not ModifiedMinecraftEntry { ModLoaders: { } modLoaders })
            return;

        foreach (var loader in modLoaders)
        {
            if (ToLoaderKind(loader.Type) is not { } selectedKind) continue;

            _installedLoaderVersions[selectedKind] = loader.Version;
            SetLoaderChecked(selectedKind, true);
        }
    }

    private static LoaderKind? ToLoaderKind(ModLoaderType type)
    {
        return type switch
        {
            ModLoaderType.Fabric => LoaderKind.Fabric,
            ModLoaderType.Forge => LoaderKind.Forge,
            ModLoaderType.NeoForge => LoaderKind.NeoForge,
            ModLoaderType.Quilt => LoaderKind.Quilt,
            ModLoaderType.OptiFine => LoaderKind.OptiFine,
            _ => null
        };
    }

    private void SetLoaderChecked(LoaderKind kind, bool value)
    {
        switch (kind)
        {
            case LoaderKind.Fabric: IsFabricSelected = value; break;
            case LoaderKind.Forge: IsForgeSelected = value; break;
            case LoaderKind.NeoForge: IsNeoForgeSelected = value; break;
            case LoaderKind.Quilt: IsQuiltSelected = value; break;
            case LoaderKind.OptiFine: IsOptiFineSelected = value; break;
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
        if (IsVersionDropDownOpen && !_isSyncingVersionText)
            _userTyping = true;
        QueueVersionRefresh();
    }

    partial void OnIsVersionDropDownOpenChanged(bool value)
    {
        if (!value)
            _userTyping = false;
        QueueVersionRefresh();
        UpdateVersionState();
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

        _vanilla = value?.Value as VersionManifestEntry;
        foreach (var kind in Enum.GetValues<LoaderKind>().Where(IsSelected))
            _ = LoadLatestAsync(kind);
        UpdateVersionState();
    }

    partial void OnIsFabricSelectedChanged(bool value)
    {
        SelectionChanged(LoaderKind.Fabric, value);
    }

    partial void OnIsForgeSelectedChanged(bool value)
    {
        SelectionChanged(LoaderKind.Forge, value);
    }

    partial void OnIsNeoForgeSelectedChanged(bool value)
    {
        SelectionChanged(LoaderKind.NeoForge, value);
    }

    partial void OnIsQuiltSelectedChanged(bool value)
    {
        SelectionChanged(LoaderKind.Quilt, value);
    }

    partial void OnIsOptiFineSelectedChanged(bool value)
    {
        SelectionChanged(LoaderKind.OptiFine, value);
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

    private async Task EnsureVersionsLoadedAsync()
    {
        var filter = SelectedVersionFilter;
        if (filter is null) return;
        var generation = ++_versionLoadGeneration;

        IsVersionsLoading = true;
        VersionsPlaceholder = CommonLanguageManager.Instance.common_loading.CurrentValue();
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
                    {
                        entries.AddRange(loaded);
                        UnlistedVersions.MergeInto(entries);
                    }
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
            VersionsPlaceholder = CommonLanguageManager.Instance.createInstance_fetchVersionsFailed.CurrentValue();
            UpdateVersionState();
        }
    }

    private void PopulateVersions(VersionFilterOption filter)
    {
        var currentId = GetCurrentVanillaId();
        if (currentId is not null &&
            _javaVersions.FirstOrDefault(version =>
                string.Equals(version.Name, currentId, StringComparison.OrdinalIgnoreCase)) is { RawType: var rawType })
        {
            var expectedKind = rawType switch
            {
                "release" => VersionFilterKind.JavaRelease,
                "snapshot" or "pending" => VersionFilterKind.JavaSnapshot,
                "old_beta" => VersionFilterKind.JavaBeta,
                "old_alpha" => VersionFilterKind.JavaAlpha,
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

        IsVersionsLoading = false;
        SelectedVersion = _categoryVersions.FirstOrDefault(option =>
                              string.Equals(option.DisplayText, currentId, StringComparison.OrdinalIgnoreCase))
                          ?? _categoryVersions.FirstOrDefault();
        if (SelectedVersion is not null)
            VersionSearchText = SelectedVersion.DisplayText;

        RefreshVersionList();
        UpdateVersionState();
    }

    private string? GetCurrentVanillaId()
    {
        return _instance.MinecraftEntry switch
        {
            ModifiedMinecraftEntry { InheritedMinecraft: { } inherited } => inherited.Version.VersionId,
            { } entry => entry.Version.VersionId,
            _ => null
        };
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
                if (!IsSelected(loaderKind))
                {
                    _selectedLoaders.Remove(loaderKind);
                    _availableLoaderVersions.Remove(loaderKind);
                    SetStatus(loaderKind, CommonLanguageManager.Instance.minecraftInstall_noLoader.CurrentValue());
                    _loadGenerations[loaderKind] = _loadGenerations.GetValueOrDefault(loaderKind) + 1;
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
        SetStatus(kind, CommonLanguageManager.Instance.minecraftInstall_fetchingLatest.CurrentValue());
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
                SetStatus(kind, CommonLanguageManager.Instance.minecraftInstall_versionUnavailable.CurrentValue());
            }
            else
            {
                var matched = MatchInstalledLoaderVersion(kind, entries);
                var entry = matched ?? entries[0];
                _availableLoaderVersions[kind] = entries;
                _selectedLoaders[kind] = entry;
                SetStatus(kind, matched is not null
                    ? string.Format(
                        CommonLanguageManager.Instance.minecraftInstall_selectedCurrentVersion.CurrentValue(),
                        MinecraftInstallationViewModel.GetLoaderVersion(kind, entry))
                    : string.Format(CommonLanguageManager.Instance.minecraftInstall_latestVersion.CurrentValue(),
                        MinecraftInstallationViewModel.GetLoaderVersion(kind, entry)));
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (IsSelected(kind) && _loadGenerations.GetValueOrDefault(kind) == generation)
            {
                _selectedLoaders.Remove(kind);
                _availableLoaderVersions.Remove(kind);
                SetStatus(kind, CommonLanguageManager.Instance.minecraftInstall_fetchFailedRetry.CurrentValue());
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
            if (kind == LoaderKind.Forge &&
                string.Equals(version.Split('-').LastOrDefault(), installedVersion, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        if (kind == LoaderKind.OptiFine)
            foreach (var entry in entries)
                if (MinecraftInstallationViewModel.GetLoaderVersion(kind, entry)
                    .StartsWith(installedVersion, StringComparison.OrdinalIgnoreCase))
                    return entry;

        return null;
    }

    private async Task<IReadOnlyList<IInstallEntry>> FetchVersionsAsync(LoaderKind kind)
    {
        return await Task.Run(async () => kind switch
        {
            LoaderKind.Fabric => (await FabricInstaller.EnumerableFabricAsync(_vanilla!.Id)).Cast<IInstallEntry>()
                .ToList(),
            LoaderKind.Forge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla!.Id)).Cast<IInstallEntry>()
                .ToList(),
            LoaderKind.NeoForge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla!.Id, true)).Cast<IInstallEntry>()
                .ToList(),
            LoaderKind.Quilt => (await QuiltInstaller.EnumerableQuiltAsync(_vanilla!.Id)).Cast<IInstallEntry>()
                .ToList(),
            LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(_vanilla!.Id)).Cast<IInstallEntry>()
                .ToList(),
            _ => []
        });
    }

    public async Task SelectLoaderVersionAsync(Control owner)
    {
        if (!CanSelectLoaderVersion) return;

        var versions = _availableLoaderVersions
            .Where(pair => IsSelected(pair.Key))
            .SelectMany(pair => pair.Value.Select(entry =>
                new LoaderVersionItem(pair.Key, entry,
                    MinecraftInstallationViewModel.GetLoaderVersion(pair.Key, entry))))
            .ToList();
        var selected = await OverlayDialog
            .ShowCustomAsync<LoaderVersionDialog, LoaderVersionDialogViewModel, LoaderVersionItem>(
                new LoaderVersionDialogViewModel(versions), owner.GetTopLevel().TryGetHostId(),
                new OverlayDialogOptions
                {
                    Title = CommonLanguageManager.Instance.minecraftInstall_selectLoaderVersion.CurrentValue(),
                    Buttons = DialogButton.None,
                    CanLightDismiss = false,
                    CanResize = false,
                    VerticalAnchor = VerticalPosition.Top,
                    VerticalOffset = 80
                });
        if (selected is null || !IsSelected(selected.Kind)) return;

        _selectedLoaders[selected.Kind] = selected.Entry;
        SetStatus(selected.Kind,
            string.Format(CommonLanguageManager.Instance.minecraftInstall_selectedVersion.CurrentValue(),
                selected.Version));
        UpdateVersionState();
    }

    public async Task ModifyAsync()
    {
        if (!CanModify || !HasChanges || SelectedVersion?.Value is not VersionManifestEntry vanilla) return;
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

    public void UninstallAllLoaders()
    {
        _updatingSelection = true;
        try
        {
            foreach (var kind in Enum.GetValues<LoaderKind>())
            {
                SetLoaderChecked(kind, false);
                _selectedLoaders.Remove(kind);
                _availableLoaderVersions.Remove(kind);
                SetStatus(kind, CommonLanguageManager.Instance.minecraftInstall_noLoader.CurrentValue());
                _loadGenerations[kind] = _loadGenerations.GetValueOrDefault(kind) + 1;
            }
        }
        finally
        {
            _updatingSelection = false;
        }

        UpdateVersionState();
    }

    private HashSet<LoaderKind> GetInstalledLoaderKinds()
    {
        return _instance.MinecraftEntry is ModifiedMinecraftEntry { ModLoaders: { } modLoaders }
            ? modLoaders.Select(loader => ToLoaderKind(loader.Type)).OfType<LoaderKind>().ToHashSet()
            : [];
    }

    private static bool LoaderVersionsMatch(LoaderKind kind, string selectedVersion, string installedVersion)
    {
        if (string.Equals(selectedVersion, installedVersion, StringComparison.OrdinalIgnoreCase)) return true;
        if (kind == LoaderKind.Forge &&
            string.Equals(selectedVersion.Split('-').LastOrDefault(), installedVersion,
                StringComparison.OrdinalIgnoreCase))
            return true;
        if (kind == LoaderKind.OptiFine &&
            selectedVersion.StartsWith(installedVersion, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private void UpdateModifyPreview()
    {
        var actions = new List<string>();
        var targetId = _vanilla?.Id;
        var currentId = GetCurrentVanillaId();
        if (!string.IsNullOrWhiteSpace(targetId) &&
            !string.Equals(targetId, currentId, StringComparison.OrdinalIgnoreCase))
            actions.Add(string.Format(
                CommonLanguageManager.Instance.minecraftInstall_previewVersionChange.CurrentValue(), currentId, targetId));

        var installedKinds = GetInstalledLoaderKinds();
        var selectedKinds = Enum.GetValues<LoaderKind>().Where(IsSelected).ToHashSet();
        if (selectedKinds.Count == 0)
        {
            if (installedKinds.Count > 0)
                actions.Add(string.Format(
                    CommonLanguageManager.Instance.minecraftInstall_previewUninstallAll.CurrentValue(),
                    string.Join("、", installedKinds)));
        }
        else
        {
            var removedKinds = installedKinds.Where(kind => !selectedKinds.Contains(kind)).ToList();
            if (removedKinds.Count > 0)
                actions.Add(string.Format(
                    CommonLanguageManager.Instance.minecraftInstall_previewUninstallLoaders.CurrentValue(),
                    string.Join("、", removedKinds)));

            var changedKinds = selectedKinds.Where(kind =>
            {
                if (!installedKinds.Contains(kind)) return true;
                if (!_installedLoaderVersions.TryGetValue(kind, out var installedVersion)) return true;
                return !_selectedLoaders.TryGetValue(kind, out var entry) ||
                       !LoaderVersionsMatch(kind, MinecraftInstallationViewModel.GetLoaderVersion(kind, entry),
                           installedVersion);
            }).ToList();
            if (changedKinds.Count > 0)
            {
                var details = changedKinds.Select(kind =>
                    _selectedLoaders.TryGetValue(kind, out var entry)
                        ? $"{kind} {MinecraftInstallationViewModel.GetLoaderVersion(kind, entry)}"
                        : kind.ToString());
                actions.Add(string.Format(
                    CommonLanguageManager.Instance.minecraftInstall_previewInstallLoaders.CurrentValue(),
                    string.Join("、", details)));
            }
        }

        ModifyPreviewText = actions.Count == 0
            ? CommonLanguageManager.Instance.minecraftInstall_previewNoChanges.CurrentValue()
            : string.Join("；", actions);
        HasChanges = actions.Count > 0;
        OnPropertyChanged(nameof(ModifyPreviewText));
        OnPropertyChanged(nameof(HasChanges));
    }

    private bool IsSelected(LoaderKind kind)
    {
        return kind switch
        {
            LoaderKind.Fabric => IsFabricSelected,
            LoaderKind.Forge => IsForgeSelected,
            LoaderKind.NeoForge => IsNeoForgeSelected,
            LoaderKind.Quilt => IsQuiltSelected,
            LoaderKind.OptiFine => IsOptiFineSelected,
            _ => false
        };
    }

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

    private bool SelectedLoadersAreReady()
    {
        return Enum.GetValues<LoaderKind>()
            .Where(IsSelected).All(_selectedLoaders.ContainsKey);
    }

    private void UpdateVersionState()
    {
        UpdateModifyPreview();
        OnPropertyChanged(nameof(HasModLoader));
        OnPropertyChanged(nameof(RequiresJava));
        OnPropertyChanged(nameof(CanSelectLoaderVersion));
        OnPropertyChanged(nameof(CanModify));
        OnPropertyChanged(nameof(CanUninstallAllLoaders));
    }

    public void Complete()
    {
        RequestClose?.Invoke(this, new VersionModifyDialogResult());
    }

    public void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}