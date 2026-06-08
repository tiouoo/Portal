using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using TioUi.Common.Helpers;
using TioUi.Shared;

namespace Portal.Classes.Entries;

public partial class ConfigEntry : ObservableObject
{
    public ConfigEntry()
    {
        PropertyChanged += OnPropertyChanged;
    }

    [ObservableProperty] private Theme _theme = Theme.Light;
    [ObservableProperty] private Color _themeColor  = Color.Parse("#1BD76A");
    [ObservableProperty] private bool _useFilePicker = true;

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Theme))
            ThemeHelper.ToggleTheme(Theme);
        else if (e.PropertyName == nameof(ThemeColor))
            ThemeHelper.SetThemeColor(ThemeColor);

        App.Method.SaveConfig();
    }
}