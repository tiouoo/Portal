using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Localization;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.InstancePages;

internal partial class AiAnalysisDialog : UserControl
{
    public AiAnalysisDialog()
    {
        InitializeComponent();
    }

    private async void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AiAnalysisDialogViewModel { IsComplete: true } viewModel)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        await topLevel.Clipboard!.SetTextAsync(viewModel.ResultText);
        topLevel.Notice(CommonLanguageManager.Instance.aiAnalysis_copied.CurrentValue(), NotificationType.Success);
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiAnalysisDialogViewModel viewModel)
            viewModel.Close();
    }
}

internal partial class AiAnalysisDialogViewModel : ObservableObject, IDialogContext
{
    private static readonly IBrush WorkingBrush = new SolidColorBrush(Color.Parse("#5B8FF9"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F44336"));
    [ObservableProperty] public partial string ResultText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } =
        CommonLanguageManager.Instance.aiAnalysis_analyzing.CurrentValue();
    [ObservableProperty] public partial bool IsComplete { get; set; }
    [ObservableProperty] public partial bool IsFailed { get; set; }

    public IBrush StatusColor => IsFailed ? ErrorBrush : IsComplete ? SuccessBrush : WorkingBrush;

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public void Append(string chunk)
    {
        ResultText += chunk;
    }

    public void Complete()
    {
        IsComplete = true;
        StatusText = CommonLanguageManager.Instance.aiAnalysis_complete.CurrentValue();
    }

    public void Fail(string message)
    {
        IsFailed = true;
        IsComplete = true;
        StatusText = string.Format(CommonLanguageManager.Instance.aiAnalysis_failed.CurrentValue(), message);
    }

    partial void OnIsCompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusColor));
    }

    partial void OnIsFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusColor));
    }
}