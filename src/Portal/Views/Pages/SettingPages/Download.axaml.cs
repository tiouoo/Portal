using System.ComponentModel;
using MinecraftLaunch.Base.Enums;
using Portal.Core.Classes.Config;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("下载设置", "设置/下载设置", "Download")]
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
        new(DownloadSourceMode.Auto, "自动（动态选择较快源）"),
        new(DownloadSourceMode.OfficialPreferred, "官方优先（失败后使用镜像）"),
        new(DownloadSourceMode.MirrorPreferred, "镜像优先（失败后使用官方）"),
        new(DownloadSourceMode.OfficialOnly, "仅原始源")
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