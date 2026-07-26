using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.ComponentModel;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("下载", "设置/下载", "Download")]
public partial class Download : DataUserControl, INotifyPropertyChanged
{
    private event PropertyChangedEventHandler? WarningPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => WarningPropertyChanged += value;
        remove => WarningPropertyChanged -= value;
    }

    public Download()
    {
        InitializeComponent();
        DataContext = this;
        Data.ConfigEntry.PropertyChanged += ConfigEntry_OnPropertyChanged;
    }

    public bool HasHighConcurrencyWarning => Data.ConfigEntry.DownloadMaxThreadCount > 70 ||
                                             Data.ConfigEntry.DownloadMaxFragmentCount > 40;

    private void ConfigEntry_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigEntry.DownloadMaxThreadCount) or nameof(ConfigEntry.DownloadMaxFragmentCount))
            WarningPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHighConcurrencyWarning)));
    }
}
