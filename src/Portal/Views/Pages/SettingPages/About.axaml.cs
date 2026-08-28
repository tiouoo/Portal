using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = Check(sender);
    }

    private async Task Check(object? sender)
    {
        if (!HyperlinkButton.IsEnabled || sender is not Control control) return;
        try
        {
            Data.UiProperty.IsLatestVersion = false;
            Data.UiProperty.FoundNewVersion = false;
            HyperlinkButton.Content = CommonLanguageManager.Instance.about_checkingUpdate.CurrentValue();
            HyperlinkButton.IsEnabled = false;
            var result = await UpdateChecker.Check(control.GetTopLevel());
            if (result == null)
            {
                Data.UiProperty.FoundNewVersion = false;
                Data.UiProperty.IsLatestVersion = false;
                return;
            }

            if (result == "latest")
            {
                Data.UiProperty.IsLatestVersion = true;
                control.GetTopLevel()?.Notice(CommonLanguageManager.Instance.update_alreadyLatest.CurrentValue(),
                    NotificationType.Success);
                return;
            }

            Data.UiProperty.NewVersion = result;
            Data.UiProperty.FoundNewVersion = true;
            _ = UpdateApp.Prepare(control.GetTopLevel());
        }
        finally
        {
            HyperlinkButton.Content = CommonLanguageManager.Instance.about_checkUpdate.CurrentValue();
            HyperlinkButton.IsEnabled = true;
        }
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
        if (sender is not Button { CommandParameter: string url }) return;

        this.AsTopLevel().Launcher.LaunchUriAsync(new Uri(url));
    }

    private async void CopyQqGroup_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is not { } clipboard) return;

        await clipboard.SetTextAsync("475032328");
        topLevel.Notice(SettingsLanguageManager.Instance.about_qqGroupCopied.CurrentValue(), NotificationType.Success);
    }
}

public class AboutViewModel
{
    public Data Data => Data.Instance;
    public string Info => $"{AppVersionService.Instance.Version.Type}.{Data.PackageType}";

    public IReadOnlyList<OpenSourceProject> OpenSourceProjects { get; } = Portal.Classes.OpenSourceProjects.All
        .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
