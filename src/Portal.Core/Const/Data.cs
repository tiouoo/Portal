using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Utilities;
using Portal.Bedrock.Standard.Interface;
using Portal.Core.Classes;
using Portal.Core.Classes.Config;
using Portal.Core.Classes.Entries;
using Portal.Core.Services;
using Tio.Avalonia.Standard.Modules.Platform;

namespace Portal.Core.Const;

public partial class Data : ObservableObject
{
    private static Data? _instance;
    private static ConfigEntry? _configEntry;

    private Data()
    {
        PropertyChanged += OnPropertyChanged;
        if (_configEntry is not null)
            _configEntry.PropertyChanged += OnPropertyChanged;
    }

    public static Data Instance
    {
        get { return _instance ??= new Data(); }
    }

    public static ConfigEntry ConfigEntry
    {
        get => _configEntry ?? throw new InvalidOperationException("Configuration has not been initialized.");
        set
        {
            if (ReferenceEquals(_configEntry, value)) return;
            if (_instance is not null && _configEntry is not null)
                _configEntry.PropertyChanged -= _instance.OnPropertyChanged;

            _configEntry = value;

            if (_instance is not null)
                _configEntry.PropertyChanged += _instance.OnPropertyChanged;
        }
    }

    public static DesktopType DesktopType => DesktopTypeDetector.CurrentPlatform;
    public static UiProperty UiProperty { get; } = UiProperty.Instance;
    public CiVersionInfo Version => AppVersionService.Instance.Version;
    [ObservableProperty] public partial string PackageType { get; set; }

    public string UserAgent => ConfigEntry.EnableCustomUserAgent && !string.IsNullOrEmpty(ConfigEntry.CustomUserAgent)
        ? ConfigEntry.CustomUserAgent
        : $"Portal/{Version.VersionTitle}";

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConfigEntry.EnableCustomUserAgent):
            case nameof(ConfigEntry.CustomUserAgent):
            case nameof(ConfigEntry.EnableProxyServer):
            case nameof(ConfigEntry.DisableSystemProxy):
            case nameof(ConfigEntry.ProxyServer):
            case nameof(ConfigEntry.EnableGithubMirror):
            case nameof(ConfigEntry.GithubMirrorUrl):
            case nameof(ConfigEntry.GithubMirrorMode):
            case nameof(ConfigEntry.EnableFragmentDownload):
            case nameof(ConfigEntry.DownloadMaxFragmentCount):
            case nameof(ConfigEntry.DownloadMaxRetryCount):
                HttpUtil.Configure(ConfigEntry.DisableSystemProxy,
                    ConfigEntry.EnableProxyServer ? ConfigEntry.ProxyServer : null, UserAgent);
                BedrockNetworkConfiguration.Configure(ConfigEntry.DisableSystemProxy,
                    ConfigEntry.EnableProxyServer ? ConfigEntry.ProxyServer : null, UserAgent,
                    ConfigEntry.EnableGithubMirror, ConfigEntry.GithubMirrorUrl,
                    ConfigEntry.GithubMirrorMode == GithubMirrorMode.Direct,
                    ConfigEntry.EnableFragmentDownload, ConfigEntry.DownloadMaxFragmentCount,
                    ConfigEntry.DownloadMaxRetryCount);
                break;
        }
    }
}