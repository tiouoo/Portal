using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using Portal.Core.Const;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class VanillaInstallation : UserControl
{
    public VanillaInstallation()
    {
        InitializeComponent();
        DataContext = new VanillaInstallationViewModel();
        Loaded += async (_, _) => await ((VanillaInstallationViewModel)DataContext).LoadVersionsAsync();
    }

    private async void VersionCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed)
            return;
        if (sender is not Control { DataContext: MinecraftVersionListItem item } || item.Entry is null)
            return;

        var page = new MinecraftInstallationPage(item.Entry);
        await OverlayDialog.ShowCustomAsync<MinecraftInstallationDialogResult>(page, page.DataContext,
            this.GetTopLevel().TryGetHostId(), new OverlayDialogOptions
            {
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                IsCloseButtonVisible = false
            });
    }
}

public partial class VanillaInstallationViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _disposed;

    public VanillaInstallationViewModel()
    {
        SelectedFilter = FilterOptions[1];
    }

    public ObservableCollection<MinecraftVersionListItem> FilteredVersions { get; } = [];

    public IReadOnlyList<MinecraftVersionFilterOption> FilterOptions { get; } =
    [
        new(CommonLanguageManager.Instance.vanillaInstall_allTypes.CurrentValue(), null),
        new(CommonLanguageManager.Instance.createInstance_versionRelease.CurrentValue(), "release"),
        new(CommonLanguageManager.Instance.createInstance_versionSnapshot.CurrentValue(), "snapshot"),
        new(CommonLanguageManager.Instance.createInstance_versionAprilFools.CurrentValue(),
            MinecraftVersionListItem.AprilFoolsType),
        new(CommonLanguageManager.Instance.createInstance_versionUnobfuscated.CurrentValue(), "unobfuscated"),
        new(CommonLanguageManager.Instance.vanillaInstall_oldBeta.CurrentValue(), "old_beta"),
        new(CommonLanguageManager.Instance.vanillaInstall_oldAlpha.CurrentValue(), "old_alpha")
    ];

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial MinecraftVersionFilterOption? SelectedFilter { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial string StatusText { get; set; } =
        CommonLanguageManager.Instance.vanillaInstall_fetchingVersions.CurrentValue();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCancellation.Cancel();
        FilteredVersions.Clear();
    }

    public async Task LoadVersionsAsync()
    {
        if (_disposed || !IsLoading) return;
        try
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

            if (!_disposed && entries.Count > 0)
                ApplyFilter();
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            StatusText = CommonLanguageManager.Instance.vanillaInstall_fetchFailed.CurrentValue();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedFilterChanged(MinecraftVersionFilterOption? value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (Data.UiProperty.MinecraftVersionManifestEntries.Count == 0)
            return;

        var versions = Data.UiProperty.MinecraftVersionManifestEntries
            .Select(MinecraftVersionListItem.FromEntry);
        if (!string.IsNullOrWhiteSpace(SearchText))
            versions = versions.Where(x => x.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        if (SelectedFilter?.Type is { } type)
            versions = type == MinecraftVersionListItem.AprilFoolsType
                ? versions.Where(x => MinecraftVersionListItem.IsAprilFoolsVersion(x.Name))
                : type == "snapshot"
                    ? versions.Where(x => x.RawType is "snapshot" or "pending")
                    : versions.Where(x => x.RawType == type);

        var results = versions.OrderByDescending(x => x.ReleaseTime).ToList();
        FilteredVersions.Clear();
        foreach (var version in results) FilteredVersions.Add(version);
        StatusText = string.Format(CommonLanguageManager.Instance.vanillaInstall_versionCount.CurrentValue(),
            results.Count);
    }
}

public sealed record MinecraftVersionFilterOption(string DisplayText, string? Type);

public sealed record MinecraftVersionListItem(
    string Name,
    string RawType,
    string Type,
    DateTime ReleaseTime,
    VersionManifestEntry? Entry = null)
{
    public const string AprilFoolsType = "april_fools";

    private static readonly HashSet<string> AprilFoolsVersionIds = new(StringComparer.Ordinal)
    {
        "26w14a",
        "25w14craftmine",
        "24w14potato",
        "23w13a_or_b",
        "22w13oneblockatatime",
        "20w14infinite",
        "3D Shareware v1.34",
        "1.RV-Pre1",
        "15w14a",
        "2.0_blue",
        "2.0_red",
        "2.0_purple"
    };

    public string RelativeReleaseTime => FormatRelativeReleaseTime(ReleaseTime);

    public string? UnlistedTag => UnlistedVersions.IsUnlistedSource(Entry) ? "UVMC" : null;
    public bool ShowUnlistedTag => UnlistedTag is not null;

    public static MinecraftVersionListItem FromEntry(VersionManifestEntry entry)
    {
        return new MinecraftVersionListItem(entry.Id, entry.Type, IsAprilFoolsVersion(entry.Id)
            ? CommonLanguageManager.Instance.createInstance_versionAprilFools.CurrentValue()
            : entry.Type switch
            {
                "release" => CommonLanguageManager.Instance.createInstance_versionRelease.CurrentValue(),
                "snapshot" => CommonLanguageManager.Instance.createInstance_versionSnapshot.CurrentValue(),
                "unobfuscated" => CommonLanguageManager.Instance.createInstance_versionUnobfuscated.CurrentValue(),
                "pending" => CommonLanguageManager.Instance.createInstance_versionSnapshot.CurrentValue(),
                "old_beta" => CommonLanguageManager.Instance.vanillaInstall_oldBeta.CurrentValue(),
                "old_alpha" => CommonLanguageManager.Instance.vanillaInstall_oldAlpha.CurrentValue(),
                _ => entry.Type
            }, entry.ReleaseTime, entry);
    }

    public static bool IsAprilFoolsVersion(string versionId)
    {
        return AprilFoolsVersionIds.Contains(versionId);
    }

    private static string FormatRelativeReleaseTime(DateTime releaseTime)
    {
        var published = releaseTime.Kind == DateTimeKind.Utc ? releaseTime.ToLocalTime() : releaseTime;
        var days = (DateTime.Today - published.Date).Days;
        return days switch
        {
            <= 0 => CommonLanguageManager.Instance.relativeTime_today.CurrentValue(),
            1 => CommonLanguageManager.Instance.relativeTime_yesterday.CurrentValue(),
            < 30 => string.Format(CommonLanguageManager.Instance.relativeTime_daysAgo.CurrentValue(), days),
            < 365 => string.Format(CommonLanguageManager.Instance.relativeTime_monthsAgo.CurrentValue(),
                Math.Max(1, days / 30)),
            < 730 => CommonLanguageManager.Instance.relativeTime_lastYear.CurrentValue(),
            _ => string.Format(CommonLanguageManager.Instance.relativeTime_yearsAgo.CurrentValue(), days / 365)
        };
    }
}
