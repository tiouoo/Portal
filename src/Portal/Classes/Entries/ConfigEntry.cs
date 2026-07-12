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
    }

    [ObservableProperty] public partial Theme Theme { get; set; } = Theme.Light;
    [ObservableProperty] public partial Color ThemeColor { get; set; } = Color.Parse("#1890ff");
    [ObservableProperty] public partial bool UseFilePicker { get; set; } = true;
    public ObservableCollection<MinecraftAccount> MinecraftAccounts { get; } = [];
    [ObservableProperty] public partial MinecraftAccount UsingMinecraftMinecraftAccount { get; set; }

    [ObservableProperty] public partial BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Default;
    [ObservableProperty] public partial string? BackgroundImagePath { get; set; }
    [ObservableProperty] public partial Color BackgroundSolidColor { get; set; } = Color.Parse("#2d2d2d");
    [ObservableProperty] public partial double AcrylicOpacity { get; set; } = 0.2;

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Theme))
            ThemeHelper.ToggleTheme(Theme);
        else if (e.PropertyName == nameof(ThemeColor))
            ThemeHelper.SetThemeColor(ThemeColor);

        switch (e.PropertyName)
        {
            case nameof(BackgroundMode):
            case nameof(BackgroundImagePath):
            case nameof(BackgroundSolidColor):
            case nameof(AcrylicOpacity):
                TabWindow.ApplyBackgroundToAllWindows();
                break;
        }

        App.Method.SaveConfig();
    }
}