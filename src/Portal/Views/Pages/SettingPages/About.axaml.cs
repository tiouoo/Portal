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

[AggregatedSearchPage("关于", "设置/关于", "About")]
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
        Data.UiProperty.IsLatestVersion = false;
        Data.UiProperty.FoundNewVersion = false;
        var channel = Data.UiProperty.OverrideUpdateChannel;
        if (channel != "nightly" && channel != "commit" && channel != "dev")
        {
            return;
        }

        HyperlinkButton.Content = "检查更新中";
        HyperlinkButton.IsEnabled = false;
        var result = await UpdateChecker.Check(sender!.AsTopLevel());
        HyperlinkButton.Content = "检查更新";
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
            sender!.AsTopLevel().Notice("当前是最新版本", NotificationType.Success);
            return;
        }

        Data.UiProperty.NewVersion = result;
        Data.UiProperty.FoundNewVersion = true;
    }

    private void UpdateChannel_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 0 && Data.Version.Type != "dev")
            _ = Check(sender!);
    }

    private async void UpdateHyperlinkButton_OnClickButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control) return;
        UpdateHyperlinkButton.IsEnabled = false;
        UpdateHyperlinkButton.Content = "正在准备更新";
        var update = await UpdateApp.Prepare(control.GetTopLevel()!);
        UpdateHyperlinkButton.Content = "下载新版本";
        UpdateHyperlinkButton.IsEnabled = true;
        if (update is null) return;

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = update.RunsInstaller
                    ? "更新安装程序已下载并校验完成。是否立即退出 Portal 并运行安装程序？"
                    : "更新已下载并准备完成。是否立即重启 Portal 并安装更新？",
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "更新准备完成",
                Mode = DialogMode.Question,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = update.RunsInstaller ? "退出并安装" : "立即重启",
                OverrideNoButtonText = "稍后",
                CanLightDismiss = false,
                CanResize = false
            });
        if (result == DialogResult.Yes) await UpdateApp.Apply(update);
    }
}

public partial class AboutViewModel : ObservableObject
{
    public Data Data => Data.Instance;
    public string Info => $"{Data.Version.Type}.{Data.PackageType}";
}
