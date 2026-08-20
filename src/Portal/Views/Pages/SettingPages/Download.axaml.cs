using System.ComponentModel;
using MinecraftLaunch.Base.Enums;
using Portal.Core.Classes.Config;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Localization;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("Download Settings", "Settings/Download Settings", "Download")]
public partial class Download : Dsc, INotifyPropertyChanged, IDisposable
{
    private bool _isDisposed;

    public Download()
    {
        InitializeComponent();
        DataContext = this;
        Data.ConfigEntry.PropertyChanged += ConfigEntry_OnPropertyChanged;
    }

    public bool HasHighConcurrencyWarning => Data.ConfigEntry.DownloadMaxThreadCount > 70 ||
                                             Data.ConfigEntry.DownloadMaxFragmentCount > 40;

    public IReadOnlyList<DownloadSourceOption> SourceOptions { get; } =
    [
        new(DownloadSourceMode.Auto, CommonLanguageManager.Instance.download_auto.CurrentValue()),
        new(DownloadSourceMode.OfficialPreferred, CommonLanguageManager.Instance.download_officialPreferred.CurrentValue()),
        new(DownloadSourceMode.MirrorPreferred, CommonLanguageManager.Instance.download_mirrorPreferred.CurrentValue()),
        new(DownloadSourceMode.OfficialOnly, CommonLanguageManager.Instance.download_officialOnly.CurrentValue())
    ];

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Data.ConfigEntry.PropertyChanged -= ConfigEntry_OnPropertyChanged;
        DataContext = null;
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => WarningPropertyChanged += value;
        remove => WarningPropertyChanged -= value;
    }

    private event PropertyChangedEventHandler? WarningPropertyChanged;

    private void ConfigEntry_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigEntry.DownloadMaxThreadCount)
            or nameof(ConfigEntry.DownloadMaxFragmentCount))
            WarningPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHighConcurrencyWarning)));
    }
}

public sealed record DownloadSourceOption(DownloadSourceMode Mode, string Name);