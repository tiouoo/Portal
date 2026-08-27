using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Classes;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Module.Update;
using Portal.Core.Services;
using Portal.Localization;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("pages_about", "pages_aboutPath", "About")]
public partial class About : Dsc
{
    public readonly AboutViewModel AboutViewModel;

    public About()
    {
        InitializeComponent();
        AboutViewModel = new AboutViewModel();
        DataContext = AboutViewModel;
        AboutViewModel.PropertyChanged += AboutViewModel_OnPropertyChanged;
    }

    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = Check(sender);
    }

    private async Task Check(object? sender)
    {
        Data.UiProperty.IsLatestVersion = false;
        Data.UiProperty.FoundNewVersion = false;
        var channel = Data.UiProperty.OverrideUpdateChannel;
        if (channel != "release" && channel != "nightly" && channel != "commit" && channel != "dev") return;

        HyperlinkButton.Content = CommonLanguageManager.Instance.about_checkingUpdate.CurrentValue();
        HyperlinkButton.IsEnabled = false;
        var result = await UpdateChecker.Check(sender!.AsTopLevel());
        HyperlinkButton.Content = CommonLanguageManager.Instance.about_checkUpdate.CurrentValue();
        HyperlinkButton.IsEnabled = true;
        if (result == null)
        {
            Data.UiProperty.FoundNewVersion = false;
            Data.UiProperty.IsLatestVersion = false;
            return;
        }

        if (result == "latest")
        {
            Data.UiProperty.IsLatestVersion = true;
            sender!.AsTopLevel().Notice(CommonLanguageManager.Instance.update_alreadyLatest.CurrentValue(),
                NotificationType.Success);
            return;
        }

        Data.UiProperty.NewVersion = result;
        Data.UiProperty.FoundNewVersion = true;
        if (sender is not null) _ = UpdateApp.Prepare(sender.AsTopLevel());
    }

    private void AboutViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AboutViewModel.SelectedUpdateSource) &&
            AppVersionService.Instance.Version.Type != "dev")
            _ = Check(UpdateSource);
    }

    private async void UpdateHyperlinkButton_OnClickButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control) return;
        UpdateHyperlinkButton.IsEnabled = false;
        UpdateHyperlinkButton.Content = CommonLanguageManager.Instance.about_preparingUpdate.CurrentValue();
        var update = await UpdateApp.Prepare(control.GetTopLevel()!);
        UpdateHyperlinkButton.Content = CommonLanguageManager.Instance.about_downloadNewVersion.CurrentValue();
        UpdateHyperlinkButton.IsEnabled = true;
    }

    private async void RestartUpdate_OnClick(object? sender, RoutedEventArgs e)
    {
        if (UpdateApp.ReadyUpdate is not { } update) return;
        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = CommonLanguageManager.Instance.about_restartReadyText.CurrentValue(),
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.about_updateReadyTitle.CurrentValue(),
                Mode = DialogMode.Question,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.about_restartNow.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.about_later.CurrentValue(),
                CanLightDismiss = false,
                CanResize = false
            });
        if (result != DialogResult.Yes) return;
        try
        {
            await UpdateApp.Apply(update);
        }
        catch (Exception exception)
        {
            Logger.Error(LogLanguageManager.Instance.about_updateStartFailed.CurrentValue(), exception);
            this.AsTopLevel().Notice(string.Format(
                CommonLanguageManager.Instance.about_cannotStartUpdate.CurrentValue(), exception.Message),
                NotificationType.Error);
        }
    }

    private void OpenLink_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton { CommandParameter: string url }) return;

        this.AsTopLevel().Launcher.LaunchUriAsync(new Uri(url));
    }
}

public class AboutViewModel : ObservableObject
{
    private UpdateSourceOption? _selectedUpdateSource;

    public AboutViewModel()
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

    public Data Data => Data.Instance;
    public string Info => $"{AppVersionService.Instance.Version.Type}.{Data.PackageType}";
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

    public IReadOnlyList<OpenSourceProject> OpenSourceProjects { get; } = Portal.Classes.OpenSourceProjects.All
        .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

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
