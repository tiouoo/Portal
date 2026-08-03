using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Portal.Const;
using Portal.Module.AggregatedSearch;
using Portal.Module.Ipc;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("其他设置", "设置/其他设置", "ApplicationDebug")]
public partial class ApplicationDebug : DataUserControl
{
    public ApplicationDebug()
    {
        InitializeComponent();
        DataContext = this;
    }

    public bool CanRegisterProtocol => ProtocolRegistration.IsSupported;
    public string RegisterProtocolButtonText => OperatingSystem.IsWindows() ? "写入注册表" : "注册协议";

    private async void RegisterProtocol_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        try
        {
            await ProtocolRegistration.RegisterAsync();
            topLevel?.Notice("portal:// 协议注册成功", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            topLevel?.Notice("已取消：写入注册表需要管理员权限", NotificationType.Warning);
        }
        catch (Exception exception)
        {
            topLevel?.Notice($"portal:// 协议注册失败：{exception.Message}", NotificationType.Error);
        }
    }
}
