using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Account;
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
    public readonly ObservableCollection<AccountBase> MinecraftAccounts = [];
    [ObservableProperty] public partial AccountBase UsingMinecraftAccount { get; set; }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Theme))
            ThemeHelper.ToggleTheme(Theme);
        else if (e.PropertyName == nameof(ThemeColor))
            ThemeHelper.SetThemeColor(ThemeColor);

        App.Method.SaveConfig();
    }
}