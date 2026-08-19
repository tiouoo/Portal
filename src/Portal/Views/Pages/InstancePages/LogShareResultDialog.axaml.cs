using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.InstancePages;

internal partial class LogShareResultDialog : UserControl
{
    public LogShareResultDialog()
    {
        InitializeComponent();
    }

    private async void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not LogShareResultItemViewModel { HasUrl: true } item)
            return;
        await CopyAsync(item.Url!);
    }

    private async void CopyAll_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LogShareResultDialogViewModel viewModel)
            return;
        var urls = string.Join('\n', viewModel.Items.Where(item => item.HasUrl).Select(item => item.Url));
        if (urls.Length > 0)
            await CopyAsync(urls);
    }

    private async Task CopyAsync(string text)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        await topLevel.Clipboard!.SetTextAsync(text);
        topLevel.Notice(CommonLanguageManager.Instance.logShare_linkCopied.CurrentValue(), NotificationType.Success);
    }

    private async void Open_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not LogShareResultItemViewModel { HasUrl: true } item)
            return;
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            await launcher.LaunchUriAsync(new Uri(item.Url!));
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogShareResultDialogViewModel viewModel)
            viewModel.Close();
    }
}

internal class LogShareResultDialogViewModel : ObservableObject, IDialogContext
{
    public LogShareResultDialogViewModel(IEnumerable<LogShareResult> results)
    {
        foreach (var result in results)
            Items.Add(LogShareResultItemViewModel.From(result));
    }

    public ObservableCollection<LogShareResultItemViewModel> Items { get; } = [];

    public bool HasAnyUrl => Items.Any(item => item.HasUrl);

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;
}

internal sealed class LogShareResultItemViewModel
{
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F44336"));

    private LogShareResultItemViewModel()
    {
    }

    public required string Platform { get; init; }
    public required bool IsSuccess { get; init; }
    public string? Url { get; init; }
    public string? Error { get; init; }

    public bool HasUrl => Url is not null;
    public bool HasError => Error is not null;
    public string StatusText => IsSuccess
        ? CommonLanguageManager.Instance.logShare_shared.CurrentValue()
        : CommonLanguageManager.Instance.logShare_failed.CurrentValue();
    public IBrush StatusColor => IsSuccess ? SuccessBrush : ErrorBrush;

    public static LogShareResultItemViewModel From(LogShareResult result)
    {
        return new LogShareResultItemViewModel
        {
            Platform = result.Platform,
            IsSuccess = result.IsSuccess,
            Url = result.Url,
            Error = result.Error
        };
    }
}