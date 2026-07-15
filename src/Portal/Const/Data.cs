using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Classes.Entries;
using Tio.Avalonia.Standard.Modules.Platform;

namespace Portal.Const;

public partial class Data : ObservableObject
{
    private static Data? _instance;

    public static Data Instance
    {
        get { return _instance ??= new Data(); }
    }

    private Data()
    {
        PropertyChanged += OnPropertyChanged;
        ConfigEntry.PropertyChanged += OnPropertyChanged;
    }

    public static ConfigEntry ConfigEntry { get; set; }
    public static DesktopType DesktopType => DesktopTypeDetector.CurrentPlatform; 
    public static UiProperty UiProperty { get; } = UiProperty.Instance;
    [ObservableProperty] public partial CiVersionInfo Version { get; set; }
    [ObservableProperty] public partial string PackageType { get; set; }
    public string UserAgent => ConfigEntry.EnableCustomUserAgent && !string.IsNullOrEmpty(ConfigEntry.CustomUserAgent) ? ConfigEntry.CustomUserAgent : $"Portal/{Version.VersionTitle}";

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConfigEntry.EnableCustomUserAgent):
            case nameof(ConfigEntry.CustomUserAgent):
                if(ConfigEntry.EnableCustomUserAgent && !string.IsNullOrEmpty(ConfigEntry.CustomUserAgent))
                    MinecraftLaunch.Utilities.HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent", ConfigEntry.CustomUserAgent);
                else
                    MinecraftLaunch.Utilities.HttpUtil.FlurlClient.Headers.AddOrReplace("User-Agent", $"Portal/{Version.VersionTitle}");
                break;
        }
    }
}