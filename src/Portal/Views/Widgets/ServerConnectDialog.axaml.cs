using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;

namespace Portal.Views.Widgets;

public partial class ServerConnectDialog : UserControl
{
    public ServerConnectDialog() => InitializeComponent();
}

public partial class ServerConnectDialogViewModel(MinecraftInstance instance) : ObservableObject, IDialogContext
{
    private readonly MinecraftInstance _instance = instance;

    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _portText = "25565";

    public string InstanceHint => $"实例：{_instance.InstanceName}";

    [RelayCommand]
    private void Confirm()
    {
        var address = Address?.Trim();
        if (string.IsNullOrEmpty(address))
            return;

        int? port = null;
        if (int.TryParse(PortText?.Trim(), out var p) && p > 0 && p < 65536)
            port = p;

        RequestClose?.Invoke(this, new ServerConnectResult(address, port ?? 25565));
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, null);

    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;
}

public sealed record ServerConnectResult(string Address, int Port);
