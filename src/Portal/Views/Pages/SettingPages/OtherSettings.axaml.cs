using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Module.Initialize;
using Portal.Core.Module.Ipc;
using Portal.Core.Module.Multiplayer;
using Portal.Core.Module.Update;
using Portal.Localization;
using Portal.Module.DefaultPage;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using Portal.Views.Pages;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("pages_otherSettings", "pages_otherSettingsPath", "OtherSettings")]
public partial class OtherSettings : Dsc
{
    private CancellationTokenSource? _relayUpdateCts;
    private bool _relaySettingsLoaded;

    public OtherSettings()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += RelaySettings_OnLoaded;
        DetachedFromVisualTree += (_, _) => CancelRelayUpdate();
    }

    public IReadOnlyList<string> HomepagePresets => CustomHomepageView.PresetNames;
    public bool IsPresetHomepage => Data.ConfigEntry.CustomHomepageType == 3;
    public bool IsOnlineHomepage => Data.ConfigEntry.CustomHomepageType == 2;
    public string SelectedHomepagePreset
    {
        get => HomepagePresets.ElementAtOrDefault(Data.ConfigEntry.CustomHomepagePreset) ?? HomepagePresets[0];
        set { var index = Array.IndexOf(HomepagePresets.ToArray(), value); if (index >= 0) Data.ConfigEntry.CustomHomepagePreset = index; }
    }

    private void RefreshHomepage_OnClick(object? sender, RoutedEventArgs e) => CustomHomepageView.RequestRefresh();

    private async void OpenPrivacyPolicy_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(
                new Uri("https://portal.tiouo.cc/policy"))!;
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Telemetry] Failed to open privacy policy: {exception.Message}");
        }
    }

    private async void OpenHomepageFile_OnClick(object? sender, RoutedEventArgs e)
    {
        var path = CustomHomepageView.LocalHomepagePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) await File.WriteAllTextAsync(path, "<TextBlock Text=\"Custom homepage\" />");
        Data.ConfigEntry.CustomHomepageType = 1;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Homepage] Failed to open local file: {exception.Message}");
        }
    }

    public static IReadOnlyList<DefaultPageRegistry.DefaultPageEntry> DefaultPages => DefaultPageRegistry.Pages;
    public UpdateSettingsViewModel UpdateSettings { get; } = new();

    public DefaultPageRegistry.DefaultPageEntry? SelectedDefaultPage
    {
        get => DefaultPageRegistry.Resolve(Data.ConfigEntry.DefaultPage);
        set
        {
            if (value != null)
                Data.ConfigEntry.DefaultPage = value.Id;
        }
    }

    public bool CanRegisterProtocol => ProtocolRegistration.IsSupported;
    public string RegisterProtocolButtonText => OperatingSystem.IsWindows()
        ? CommonLanguageManager.Instance.appDebug_writeRegistry.CurrentValue()
        : CommonLanguageManager.Instance.appDebug_registerProtocol.CurrentValue();
    public Logger.LogLevel[] LogLevels { get; } = Enum.GetValues<Logger.LogLevel>();

    private async void RegisterProtocol_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        try
        {
            await ProtocolRegistration.RegisterAsync();
            topLevel?.Notice(CommonLanguageManager.Instance.appDebug_registerSuccess.CurrentValue(),
                NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            topLevel?.Notice(CommonLanguageManager.Instance.appDebug_registerCancelled.CurrentValue(),
                NotificationType.Warning);
        }
        catch (Exception exception)
        {
            topLevel?.Notice(string.Format(
                CommonLanguageManager.Instance.appDebug_registerFailed.CurrentValue(), exception.Message),
                NotificationType.Error);
        }
    }

    private async void RelaySettings_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_relaySettingsLoaded) return;
        _relaySettingsLoaded = true;

        await ShowCachedRelayNodesAsync();

        if (Data.ConfigEntry.GravityConeRelayAutoUpdate)
            await StartRelayUpdateAsync(false);
    }

    private async void RelaySources_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_relaySettingsLoaded || !Data.ConfigEntry.GravityConeRelayAutoUpdate) return;

        CancelRelayUpdate();
        var cancellation = _relayUpdateCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(700, cancellation.Token);
            await UpdateRelayNodesAsync(cancellation, false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.CompareExchange(ref _relayUpdateCts, null, cancellation);
            cancellation.Dispose();
        }
    }

    private async void UpdateRelayNodes_OnClick(object? sender, RoutedEventArgs e)
        => await StartRelayUpdateAsync(true);

    private async Task StartRelayUpdateAsync(bool showSuccess)
    {
        CancelRelayUpdate();
        var cancellation = _relayUpdateCts = new CancellationTokenSource();
        await UpdateRelayNodesAsync(cancellation, showSuccess);
    }

    private void CancelRelayUpdate()
    {
        var cancellation = Interlocked.Exchange(ref _relayUpdateCts, null);
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The completed update won the race and already released its token source.
        }
    }

    private async Task UpdateRelayNodesAsync(CancellationTokenSource cancellation, bool showSuccess)
    {
        UpdateRelayNodesButton.IsEnabled = false;
        var topLevel = TopLevel.GetTopLevel(this);
        try
        {
            var relays = await GravityConeRelayClient.Instance.UpdateRelaySourcesAsync(cancellation.Token);
            if (!ReferenceEquals(_relayUpdateCts, cancellation)) return;

            RelayNodesResultTextBox.Text = string.Join(Environment.NewLine, relays);
            ConfigSaver.SaveConfig();
            if (showSuccess)
                topLevel?.Notice(
                    SettingsLanguageManager.Instance.applicationdebug_relayNodesUpdated.CurrentValue(),
                    NotificationType.Success);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.Warning($"[RelayNodes] Update failed: {exception.Message}");
            await ShowCachedRelayNodesAsync();
            if (showSuccess)
                topLevel?.Notice(exception.Message, NotificationType.Error);
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _relayUpdateCts, null, cancellation) == cancellation)
                UpdateRelayNodesButton.IsEnabled = true;
            cancellation.Dispose();
        }
    }

    private async Task ShowCachedRelayNodesAsync()
    {
        var cached = await GravityConeRelayClient.Instance.TryReadCacheAsync(CancellationToken.None);
        if (cached is not null)
            RelayNodesResultTextBox.Text = string.Join(Environment.NewLine, cached);
    }
}

public sealed class UpdateSettingsViewModel : ObservableObject
{
    private UpdateSourceOption? _selectedUpdateSource;

    public UpdateSettingsViewModel()
    {
        var release = SettingsLanguageManager.Instance.about_channelRelease.CurrentValue();
        var nightly = SettingsLanguageManager.Instance.about_channelDailyBuild.CurrentValue();
        var commit = SettingsLanguageManager.Instance.about_channelCommitBuild.CurrentValue();
        var githubRelease = new UpdateSourceOption(release, $"GitHub → {release}", UpdateSource.Github, "release");
        var githubNightly = new UpdateSourceOption(nightly, $"GitHub → {nightly}", UpdateSource.Github, "nightly");
        var githubCommit = new UpdateSourceOption(commit, $"GitHub → {commit}", UpdateSource.Github, "commit");
        var cnbRelease = new UpdateSourceOption(release, $"Cnb → {release}", UpdateSource.Cnb, "release");

        UpdateSources =
        [
            new UpdateSourceOption("Cnb", children: [cnbRelease]),
            new UpdateSourceOption("GitHub", children: [githubRelease, githubNightly, githubCommit])
        ];

        _selectedUpdateSource = Data.ConfigEntry.UpdateSource == UpdateSource.Cnb
            ? cnbRelease
            : Data.UiProperty.OverrideUpdateChannel switch
            {
                "nightly" => githubNightly,
                "commit" => githubCommit,
                _ => githubRelease
            };
        ApplyUpdateSource(_selectedUpdateSource);
    }

    public IReadOnlyList<UpdateSourceOption> UpdateSources { get; }
    public bool IsGithubUpdateSource => SelectedUpdateSource?.Source == UpdateSource.Github;

    public UpdateSourceOption? SelectedUpdateSource
    {
        get => _selectedUpdateSource;
        set
        {
            if (ReferenceEquals(_selectedUpdateSource, value) || value?.Source is null) return;
            _selectedUpdateSource = value;
            ApplyUpdateSource(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGithubUpdateSource));
        }
    }

    private static void ApplyUpdateSource(UpdateSourceOption option)
    {
        if (option.Source is not { } source || option.Channel is not { } channel) return;
        Data.ConfigEntry.UpdateSource = source;
        Data.ConfigEntry.UpdateChannel = channel;
        Data.UiProperty.OverrideUpdateChannel = channel;
        UpdateApp.NotifyUpdateSelectionChanged();
    }
}

public sealed class UpdateSourceOption
{
    public UpdateSourceOption(string displayName, string? pathDisplayName = null,
        UpdateSource? source = null, string? channel = null,
        IReadOnlyList<UpdateSourceOption>? children = null)
    {
        DisplayName = displayName;
        PathDisplayName = pathDisplayName ?? displayName;
        Source = source;
        Channel = channel;
        Children = children ?? [];
    }

    public string DisplayName { get; }
    public string PathDisplayName { get; }
    public UpdateSource? Source { get; }
    public string? Channel { get; }
    public IReadOnlyList<UpdateSourceOption> Children { get; }
    public bool IsSelectable => Source is not null;
}
