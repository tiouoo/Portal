using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Services;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Interfaces;

namespace Portal.Views.Components.Operations.Account;

public partial class BedrockMicrosoft : UserControl
{
    private bool _started;

    public BedrockMicrosoft()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_started) return;
            _started = true;
            _ = ((BedrockMicrosoftViewModel)DataContext!).AuthenticateAsync();
        };
    }

    private void CopyCode(object? sender, RoutedEventArgs e)
    {
        var viewModel = (BedrockMicrosoftViewModel)DataContext!;
        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(viewModel.Code);
        TopLevel.GetTopLevel(this)!.Notice("已复制到剪贴板", NotificationType.Success);
    }

    private void OpenBrowser(object? sender, RoutedEventArgs e)
    {
        var viewModel = (BedrockMicrosoftViewModel)DataContext!;
        _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(viewModel.Url));
    }
}

public partial class BedrockMicrosoftViewModel : ObservableObject, IDialogContext
{
    private readonly CancellationTokenSource _cancellation = new();
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial bool IsError { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "正在请求微软设备验证码...";
    [ObservableProperty] public partial string Error { get; set; } = string.Empty;
    [ObservableProperty] public partial string Code { get; set; } = string.Empty;
    [ObservableProperty] public partial string Url { get; set; } = string.Empty;
    public RelayCommand Cancel => new(Close);

    public void Close()
    {
        _cancellation.Cancel();
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public async Task AuthenticateAsync()
    {
        try
        {
            var account = await new BedrockAuthenticationService().SignInAsync((url, code) =>
            {
                Url = url;
                Code = code;
                Message = "打开微软验证页面并输入下方代码，完成 Xbox 账户关联。";
                IsReady = true;
            }, _cancellation.Token);
            RequestClose?.Invoke(this, account);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            IsError = true;
            Error = exception.Message;
        }
    }
}