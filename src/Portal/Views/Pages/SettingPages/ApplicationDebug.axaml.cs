using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Module.Ipc;
using Portal.Localization;
using Portal.Module.DefaultPage;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("pages_otherSettings", "pages_otherSettingsPath", "ApplicationDebug")]
public partial class ApplicationDebug : Dsc
{
    public ApplicationDebug()
    {
        InitializeComponent();
        DataContext = this;
    }

    public static IReadOnlyList<DefaultPageRegistry.DefaultPageEntry> DefaultPages => DefaultPageRegistry.Pages;
    public UpdateSettingsViewModel UpdateSettings { get; } = new();

    public DefaultPageRegistry.DefaultPageEntry? SelectedDefaultPage
    {
        get => DefaultPages.FirstOrDefault(page => page.PageType.AssemblyQualifiedName == Data.ConfigEntry.DefaultPage);
        set
        {
            if (value != null)
                Data.ConfigEntry.DefaultPage = value.PageType.AssemblyQualifiedName!;
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
        Data.UiProperty.OverrideUpdateChannel = channel;
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
