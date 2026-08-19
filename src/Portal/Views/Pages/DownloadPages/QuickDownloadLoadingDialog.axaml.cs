using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Localization;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

public partial class QuickDownloadLoadingDialog : UserControl
{
    public QuickDownloadLoadingDialog()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as QuickDownloadLoadingDialogViewModel)?.Close();
    }
}

public partial class QuickDownloadLoadingDialogViewModel(string title) : ObservableObject, IDialogContext
{
    public string Title { get; } = title;
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial string Message { get; set; } =
        CommonLanguageManager.Instance.quickDownload_fetchingFiles.CurrentValue();

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public void Fail()
    {
        IsLoading = false;
        Message = CommonLanguageManager.Instance.quickDownload_fetchFailed.CurrentValue();
    }
}