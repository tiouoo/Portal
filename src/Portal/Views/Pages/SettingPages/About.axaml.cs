using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Const;
using Portal.Module.Update;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Views.Pages.SettingPages;

public partial class About : DataUserControl
{
    public readonly AboutViewModel AboutViewModel;

    public About()
    {
        InitializeComponent();
        AboutViewModel = new AboutViewModel();
        DataContext = AboutViewModel;
        if (Data.Version.Type == "dev")
            AboutViewModel.IsDev = true;
    }

    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = Check(sender);
    }

    private async Task Check(object? sender)
    {
        Data.UiProperty.IsLatestVersion = false;
        Data.UiProperty.FoundNewVersion = false;
        if (Data.UiProperty.OverrideUpdateChannel == "dev")
        {
            sender!.AsTopLevel().Notice("开发版本(dev)不能更新", NotificationType.Error);
            return;
        }

        var channel = Data.UiProperty.OverrideUpdateChannel;
        if (channel != "nightly" && channel != "commit")
        {
            return;
        }

        HyperlinkButton.Content = "检查更新中";
        HyperlinkButton.IsEnabled = false;
        var result = await CheckUpdate.Main(sender!.AsTopLevel());
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
        if (e.RemovedItems.Count > 0)
            _ = Check(sender!);
    }

    private void UpdateHyperlinkButton_OnClickButton_OnClick(object? sender, RoutedEventArgs e)
    {
    }
}

public partial class AboutViewModel : ObservableObject
{
    public Data Data => Data.Instance;

    [ObservableProperty] public partial bool IsDev { get; set; }
}