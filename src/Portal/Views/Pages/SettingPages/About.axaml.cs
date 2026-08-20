using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
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

[AggregatedSearchPage("About", "Settings/About", "About")]
public partial class About : Dsc
{
    public readonly AboutViewModel AboutViewModel;

    public About()
    {
        InitializeComponent();
        AboutViewModel = new AboutViewModel();
        DataContext = AboutViewModel;
        if (Data.ConfigEntry.UpdateSource != UpdateSource.Github)
            Data.UiProperty.OverrideUpdateChannel = "release";
        UpdateChannel.IsEnabled = Data.ConfigEntry.UpdateSource == UpdateSource.Github;
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
    }

    private void UpdateChannel_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 0 && AppVersionService.Instance.Version.Type != "dev")
            _ = Check(sender!);
    }

    private void UpdateSource_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var supportsPreviewChannels = Data.ConfigEntry.UpdateSource == UpdateSource.Github;
        if (!supportsPreviewChannels) Data.UiProperty.OverrideUpdateChannel = "release";
        UpdateChannel.IsEnabled = supportsPreviewChannels;
        if (e.RemovedItems.Count > 0 && AppVersionService.Instance.Version.Type != "dev") _ = Check(sender!);
    }

    private async void UpdateHyperlinkButton_OnClickButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control) return;
        UpdateHyperlinkButton.IsEnabled = false;
        UpdateHyperlinkButton.Content = CommonLanguageManager.Instance.about_preparingUpdate.CurrentValue();
        var update = await UpdateApp.Prepare(control.GetTopLevel()!);
        UpdateHyperlinkButton.Content = CommonLanguageManager.Instance.about_downloadNewVersion.CurrentValue();
        UpdateHyperlinkButton.IsEnabled = true;
        if (update is null) return;

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = update.RunsInstaller
                    ? CommonLanguageManager.Instance.about_installerReadyText.CurrentValue()
                    : CommonLanguageManager.Instance.about_restartReadyText.CurrentValue(),
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.about_updateReadyTitle.CurrentValue(),
                Mode = DialogMode.Question,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = update.RunsInstaller
                    ? CommonLanguageManager.Instance.about_exitAndInstall.CurrentValue()
                    : CommonLanguageManager.Instance.about_restartNow.CurrentValue(),
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
    public Data Data => Data.Instance;
    public string Info => $"{AppVersionService.Instance.Version.Type}.{Data.PackageType}";

    public IReadOnlyList<OpenSourceProject> OpenSourceProjects { get; } =
    [
        new("Avalonia", "MIT License", "https://github.com/AvaloniaUI/Avalonia"),
        new("AsyncImageLoader.Avalonia", "MIT License", "https://github.com/AvaloniaUtils/AsyncImageLoader.Avalonia"),
        new("CommunityToolkit.Mvvm", "MIT License", "https://github.com/CommunityToolkit/dotnet"),
        new("DotNet.Bundle", "MIT License", "https://github.com/Tyrrrz/DotnetBundle"),
        new("fNbt", "BSD 3-Clause License", "https://github.com/mstefarov/fNbt"),
        new("Flurl.Http", "MIT License", "https://flurl.dev/"),
        new("Hardware.Info", "MIT License", "https://github.com/Jinjinov/Hardware.Info"),
        new("HotAvalonia", "MIT License", "https://github.com/Kira-NT/HotAvalonia"),
        new("Html Agility Pack", "MIT License", "https://github.com/zzzprojects/html-agility-pack"),
        new("Microsoft.Data.Sqlite", "MIT License", "https://github.com/dotnet/efcore"),
        new("SQLitePCLRaw", "Apache License 2.0", "https://github.com/ericsink/SQLitePCL.raw"),
        new("NbtToolkit", "MIT License", "https://github.com/gaviny82/NbtToolkit"),
        new("PeNet", "Apache License 2.0", "https://github.com/secana/PeNet"),
        new("PinYinConverterCore", "MIT License", "https://github.com/netcorepal/PinYinConverterCore"),
        new("PolySharp", "MIT License", "https://github.com/Sergio0694/PolySharp"),
        new("SharpCompress", "MIT License", "https://github.com/adamhathcock/sharpcompress"),
        new("SkiaSharp", "MIT License", "https://github.com/mono/SkiaSharp"),
        new("SmoothScroll.Avalonia", "MIT License", "https://github.com/alienator88/SmoothScroll.Avalonia"),
        new("Tomlyn", "BSD 2-Clause License", "https://github.com/xoofx/Tomlyn"),
        new("BLoader", "GNU GPL v3.0", "https://github.com/Chlna6666/BLoader"),
        new("WineGDK", "GNU LGPL v2.1+", "https://github.com/winegdk/winegdk"),
        new("MinecraftLaunch", "MIT License", "https://github.com/tiouoo/MinecraftLaunch"),
        new("LiteSkinViewer", "MIT License", "https://github.com/tiouoo/LiteSkinViewer"),
        new("Tio.Avalonia.Standard", "MIT License", "https://github.com/tiouoo/Tio.Avalonia.Standard"),
        new("TioUi.Avalonia", "MIT License", "https://github.com/tiouoo/TioUi.Avalonia"),
        new("PreLoadCpp", "GNU GPL v3.0", "https://github.com/Round-Studio/PreLoadCpp"),
        new("Uwp.Injector", "Apache License 2.0", "https://github.com/Round-Studio/Uwp.Injector"),
        new("GravityCone", "MIT License", "https://github.com/Tianpao/GravityCone"),
        new("EasyTier", "GNU LGPL v3.0", "https://github.com/EasyTier/EasyTier"),
        new("GDK-Proton", CommonLanguageManager.Instance.about_noLicense.CurrentValue(),
            "https://github.com/Weather-OS/GDK-Proton")
    ];
}

public sealed record OpenSourceProject(string Name, string License, string Url);