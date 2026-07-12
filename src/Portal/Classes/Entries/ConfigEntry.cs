using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Account;
using Portal.Views;
using TioUi.Common.Helpers;
using TioUi.Shared;

namespace Portal.Classes.Entries;

public partial class ConfigEntry : ObservableObject
{
    public ConfigEntry()
    {
        PropertyChanged += OnPropertyChanged;
        MinecraftAccounts.CollectionChanged += (_, _) => App.Method.SaveConfig();
        AuthServers.CollectionChanged += (_, _) => App.Method.SaveConfig();
    }

    [ObservableProperty] public partial Theme Theme { get; set; } = Theme.Light;
    [ObservableProperty] public partial Color ThemeColor { get; set; } = Color.Parse("#1890ff");
    [ObservableProperty] public partial bool UseFilePicker { get; set; } = true;
    public ObservableCollection<MinecraftAccount> MinecraftAccounts { get; } = [];
    public ObservableCollection<AuthServer> AuthServers { get; } = [];
    [ObservableProperty] public partial MinecraftAccount? UsingMinecraftMinecraftAccount { get; set; }

    [ObservableProperty] public partial BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Default;
    [ObservableProperty] public partial string? BackgroundImagePath { get; set; }
    [ObservableProperty] public partial Color BackgroundSolidColor { get; set; } = Color.Parse("#2d2d2d");
    [ObservableProperty] public partial double AcrylicOpacity { get; set; } = 0.2;
    [ObservableProperty] public partial bool AcrylicEnabled { get; set; } = false;
    [ObservableProperty] public partial bool ImageEnabled { get; set; } = false;
    [ObservableProperty] public partial double ImageOpacity { get; set; } = 0.0;

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Theme):
                ThemeHelper.ToggleTheme(Theme);
                break;
            case nameof(ThemeColor):
                ThemeHelper.SetThemeColor(ThemeColor);
                break;
            case nameof(BackgroundMode):
            case nameof(BackgroundSolidColor):
            case nameof(AcrylicOpacity):
            case nameof(ImageOpacity):
                TabWindow.ApplyBackgroundToAllWindows();
                break;
            case nameof(BackgroundImagePath):
                if (!string.IsNullOrEmpty(BackgroundImagePath) && ImageEnabled)
                    BackgroundMode = BackgroundMode.Image;
                TabWindow.ApplyBackgroundToAllWindows();
                break;
            case nameof(AcrylicEnabled):
                if (AcrylicEnabled) ImageEnabled = false;
                BackgroundMode = AcrylicEnabled ? BackgroundMode.Acrylic : BackgroundMode.Default;
                break;
            case nameof(ImageEnabled):
                if (ImageEnabled) AcrylicEnabled = false;
                BackgroundMode = ImageEnabled ? BackgroundMode.Image : BackgroundMode.Default;
                break;
        }

        App.Method.SaveConfig();
    }
}