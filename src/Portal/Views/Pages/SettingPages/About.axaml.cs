using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Const;
using Portal.Module.AggregatedSearch;
using Portal.Module.Update;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("关于应用", "设置/关于应用", "About")]
public partial class About : DataUserControl
{
    public readonly AboutViewModel AboutViewModel;

    public About()
    {
        InitializeComponent();
        AboutViewModel = new AboutViewModel();
        DataContext = AboutViewModel;
    }

    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = Check(sender);
    }

    private async Task Check(object? sender)
    {
        var channel = Data.UiProperty.OverrideUpdateChannel;
        if (channel != "nightly" && channel != "commit" && channel != "dev")
        {
            return;
        }

        HyperlinkButton.Content = "检查更新中";
        HyperlinkButton.IsEnabled = false;
        await UpdateApp.Instance.CheckAndDownloadAsync(sender!.AsTopLevel());
        HyperlinkButton.Content = "检查更新";
        HyperlinkButton.IsEnabled = true;
    }

    private void UpdateChannel_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 0 && Data.Version.Type != "dev")
            _ = Check(sender!);
    }

    private async void UpdateHyperlinkButton_OnClickButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control) return;
        if (UpdateApp.Instance.State != UpdateState.ReadyToRestart)
        {
            await UpdateApp.Instance.HandleActionAsync(control.GetTopLevel()!);
            return;
        }

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = "增量更新包已下载并准备完成。是否立即重启 Portal 并安装更新？",
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "更新准备完成",
                Mode = DialogMode.Question,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "立即重启",
                OverrideNoButtonText = "稍后",
                CanLightDismiss = false,
                CanResize = false
            });
        if (result == DialogResult.Yes) await UpdateApp.Instance.ApplyAsync();
    }

    private void OpenLink_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton { CommandParameter: string url }) return;

        this.AsTopLevel().Launcher.LaunchUriAsync(new Uri(url));
    }
}

public partial class AboutViewModel : ObservableObject
{
    public Data Data => Data.Instance;
    public UpdateApp Updates => UpdateApp.Instance;
    public string Info => $"{Data.Version.Type}.{Data.PackageType}";
}
