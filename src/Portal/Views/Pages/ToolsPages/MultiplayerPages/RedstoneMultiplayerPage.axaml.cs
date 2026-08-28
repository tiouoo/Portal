using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Const;
using Portal.Core.Module.Multiplayer;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Views.Pages.ToolsPages.MultiplayerPages;

public partial class RedstoneMultiplayerPage : UserControl
{
    private readonly RedstoneMultiplayerViewModel _viewModel;

    public RedstoneMultiplayerPage()
    {
        InitializeComponent();
        _viewModel = new RedstoneMultiplayerViewModel();
        DataContext = _viewModel;
    }

    public RedstoneMultiplayerViewModel ViewModel => _viewModel;

    private async void CopyAddress_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (string.IsNullOrWhiteSpace(_viewModel.PublicAddress)) return;
        await clipboard.SetTextAsync(_viewModel.PublicAddress);
        TopLevel.GetTopLevel(this)?.Notice(
            CommonLanguageManager.Instance.multiplayer_roomCodeCopied.CurrentValue(), NotificationType.Success);
    }

    private void OpenLogs_OnClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.OpenLogs();
    }
}

public sealed class RedstonePortOption
{
    public required bool IsManual { get; init; }
    public DetectedLanPort? Port { get; init; }
    public required string Display { get; init; }
}

public sealed class RedstoneNodeOption
{
    public required bool IsAuto { get; init; }
    public HongshiNode? Node { get; init; }
    public required string Display { get; init; }
}

public partial class RedstoneMultiplayerViewModel : ObservableObject, IAsyncDisposable, IMultiplayerPageLifecycle
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HongshiMultiplayerService _service = HongshiMultiplayerService.Instance;
    private bool _disposed;
    private bool _isActive;

    public RedstoneMultiplayerViewModel()
    {
        _service.StateChanged += OnServiceStateChanged;
        ManualPort = "25565";
        RefreshFromService();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadCard))]
    [NotifyPropertyChangedFor(nameof(ShowDownloadBanner))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(DownloadProgress))]
    [NotifyPropertyChangedFor(nameof(ShowBusy))]
    [NotifyPropertyChangedFor(nameof(ShowOpen))]
    [NotifyPropertyChangedFor(nameof(ShowForm))]
    [NotifyPropertyChangedFor(nameof(ShowClosedWarning))]
    [NotifyPropertyChangedFor(nameof(ShowErrorCard))]
    [NotifyPropertyChangedFor(nameof(ErrorMessage))]
    [NotifyPropertyChangedFor(nameof(PublicAddress))]
    [NotifyPropertyChangedFor(nameof(PortChanged))]
    [NotifyPropertyChangedFor(nameof(NodeLabelText))]
    [NotifyPropertyChangedFor(nameof(LocalPortDisplay))]
    [NotifyPropertyChangedFor(nameof(BusyText))]
    [NotifyPropertyChangedFor(nameof(BusyHintText))]
    private partial HongshiState? State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateTunnel))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateTunnel))]
    public partial bool IsNodesLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualPortSelected))]
    [NotifyPropertyChangedFor(nameof(CanCreateTunnel))]
    public partial string ManualPort { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualPortSelected))]
    [NotifyPropertyChangedFor(nameof(CanCreateTunnel))]
    public partial RedstonePortOption? SelectedPortOption { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateTunnel))]
    public partial RedstoneNodeOption? SelectedNodeOption { get; set; }

    public bool ShowUnsupported => State is { Supported: false };
    public bool ShowDownloadCard => IsDownloading || ShowDownloadBanner;
    public bool ShowDownloadBanner => State is { Supported: true, BinaryInstalled: false };
    public bool IsDownloading => State?.Status == HongshiStatus.Downloading;
    public int DownloadProgress => State?.DownloadProgress ?? 0;
    public bool ShowBusy => State?.Status is HongshiStatus.SelectingNode or HongshiStatus.Starting;
    public bool ShowOpen => State?.Status == HongshiStatus.Open;
    public bool ShowForm => State is { Supported: true, BinaryInstalled: true } &&
                            State.Status is HongshiStatus.Idle or HongshiStatus.Closed or HongshiStatus.Error;
    public bool ShowClosedWarning => State?.Status == HongshiStatus.Closed;
    public bool ShowErrorCard => State?.Status == HongshiStatus.Error;
    public string ErrorMessage => State?.ErrorMessage ?? string.Empty;
    public string PublicAddress => State?.PublicAddress ?? string.Empty;
    public bool PortChanged => State?.PortChanged ?? false;

    public string NodeLabelText => State?.Node is { } node
        ? string.Format(CommonLanguageManager.Instance.multiplayer_redstoneNodeLabel.CurrentValue(), node.Name,
            node.LatencyMs ?? 0, node.Cached
                ? CommonLanguageManager.Instance.multiplayer_redstoneCached.CurrentValue()
                : string.Empty)
        : string.Empty;

    public string LocalPortDisplay => State?.LocalPort is { } port
        ? $"127.0.0.1:{port}"
        : string.Empty;

    public string BusyText => State?.Status == HongshiStatus.SelectingNode
        ? CommonLanguageManager.Instance.multiplayer_redstoneSelectingNode.CurrentValue()
        : CommonLanguageManager.Instance.multiplayer_redstoneCreatingTunnel.CurrentValue();

    public string BusyHintText => State?.Status == HongshiStatus.SelectingNode
        ? string.Empty
        : CommonLanguageManager.Instance.multiplayer_redstonePortHint.CurrentValue();

    public bool IsManualPortSelected => SelectedPortOption?.IsManual ?? true;

    public int? EffectiveLocalPort
    {
        get
        {
            if (SelectedPortOption is { IsManual: false, Port: { } detected }) return detected.Port;
            return int.TryParse(ManualPort.Trim(), out var port) && port is >= 1 and <= 65535 ? port : null;
        }
    }

    public bool CanCreateTunnel => !IsBusy && !IsNodesLoading && EffectiveLocalPort is not null &&
                                   NodeOptions.Count > 0;

    public ObservableCollection<DetectedLanPort> DetectedPorts { get; } = [];
    public ObservableCollection<HongshiNode> Nodes { get; } = [];

    public ObservableCollection<RedstonePortOption> DetectedPortOptions { get; } = [];
    public ObservableCollection<RedstoneNodeOption> NodeOptions { get; } = [];

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        if (!_isActive) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isActive) return;
            RefreshFromService();
        });
    }

    public void Activate()
    {
        _isActive = true;
        RefreshFromService();
        _ = LoadNodesAsync(forceRefresh: false);
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    private void RefreshFromService()
    {
        State = _service.GetState();
        RefreshDetectedPorts();
    }

    private void RefreshDetectedPorts()
    {
        var selected = SelectedPortOption?.IsManual == false ? SelectedPortOption.Port?.InstanceId : null;
        var ports = _service.GetDetectedPorts();
        DetectedPorts.Clear();
        foreach (var port in ports) DetectedPorts.Add(port);

        DetectedPortOptions.Clear();
        DetectedPortOptions.Add(new RedstonePortOption
        {
            IsManual = true,
            Display = CommonLanguageManager.Instance.multiplayer_redstoneManualPort.CurrentValue()
        });
        foreach (var port in ports)
        {
            DetectedPortOptions.Add(new RedstonePortOption
            {
                IsManual = false,
                Port = port,
                Display = string.Format(
                    CommonLanguageManager.Instance.multiplayer_redstoneDetectedPort.CurrentValue(),
                    port.InstanceName, port.Port)
            });
        }

        SelectedPortOption = selected is not null
            ? DetectedPortOptions.FirstOrDefault(option =>
                !option.IsManual && option.Port?.InstanceId == selected)
            : DetectedPortOptions.FirstOrDefault(option => !option.IsManual) ?? DetectedPortOptions.FirstOrDefault();
        OnPropertyChanged(nameof(IsManualPortSelected));
        OnPropertyChanged(nameof(CanCreateTunnel));
    }

    private async Task LoadNodesAsync(bool forceRefresh)
    {
        if (IsNodesLoading) return;
        IsNodesLoading = true;
        try
        {
            var nodes = await _service.GetNodesAsync(forceRefresh, _lifetime.Token);
            var selectedName = SelectedNodeOption?.IsAuto == false
                ? SelectedNodeOption.Node?.Name
                : Data.ConfigEntry.RedstoneSelectedNode;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Nodes.Clear();
                foreach (var node in nodes) Nodes.Add(node);

                NodeOptions.Clear();
                NodeOptions.Add(new RedstoneNodeOption
                {
                    IsAuto = true,
                    Display = CommonLanguageManager.Instance.multiplayer_redstoneNodeAuto.CurrentValue()
                });
                foreach (var node in nodes)
                {
                    var display = node.Reachable
                        ? string.Format(CommonLanguageManager.Instance.multiplayer_redstoneNodeLabel.CurrentValue(),
                            node.Name, node.LatencyMs ?? 0, node.Cached
                                ? CommonLanguageManager.Instance.multiplayer_redstoneCached.CurrentValue()
                                : string.Empty)
                        : $"{node.Name} — {CommonLanguageManager.Instance.multiplayer_redstoneUnreachable.CurrentValue()}";
                    NodeOptions.Add(new RedstoneNodeOption { IsAuto = false, Node = node, Display = display });
                }

                SelectedNodeOption = NodeOptions.FirstOrDefault(option =>
                    !option.IsAuto && option.Node?.Name == selectedName) ?? NodeOptions.FirstOrDefault();
                OnPropertyChanged(nameof(CanCreateTunnel));
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.Warning($"[RedStone] Failed to load nodes: {exception.Message}");
            Notify(exception.Message, NotificationType.Error);
        }
        finally
        {
            IsNodesLoading = false;
        }
    }

    [RelayCommand]
    private Task RefreshNodesAsync() => LoadNodesAsync(true);

    [RelayCommand]
    private async Task DownloadKernelAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        var taskName = CommonLanguageManager.Instance.multiplayer_redstoneDownloadKernel.CurrentValue();
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = taskName,
            Description = CommonLanguageManager.Instance.multiplayer_preparingDownload.CurrentValue(),
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.multiplayer_cancelDownload.CurrentValue(),
                    Description = CommonLanguageManager.Instance.multiplayer_cancelComponentDownload.CurrentValue(),
                    IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) =>
                    {
                        managedTask.RequestCancellation();
                        return Task.CompletedTask;
                    },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, async context =>
        {
            var progress = new Progress<HongshiDownloadProgress>(item =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                    if (item.Percent is { } percent) context.ReportProgress(percent / 100.0);
                });
            });
            await _service.DownloadAsync(progress, context.CancellationToken);
            context.ReportProgress(1);
        });
        task.Start();
        _ = ObserveInstallationAsync(task);
    }

    private async Task ObserveInstallationAsync(ManagedTask task)
    {
        try
        {
            await task.Completion;
        }
        catch (Exception exception)
        {
            Logger.Error($"[RedStone] Kernel download task failed: {exception}");
        }
        finally
        {
            IsBusy = false;
            RefreshFromService();
        }
    }

    [RelayCommand]
    private async Task CreateTunnelAsync()
    {
        if (!CanCreateTunnel) return;
        var port = EffectiveLocalPort!.Value;
        var nodeName = SelectedNodeOption?.IsAuto == false ? SelectedNodeOption.Node?.Name : null;
        var instanceId = SelectedPortOption?.IsManual == false ? SelectedPortOption.Port?.InstanceId : null;
        if (nodeName is not null) Data.ConfigEntry.RedstoneSelectedNode = nodeName;
        await RunOperationAsync(() => _service.StartAsync(port, nodeName, instanceId, _lifetime.Token));
    }

    [RelayCommand]
    private async Task RestartTunnelAsync()
    {
        await RunOperationAsync(_service.StopAsync);
        await CreateTunnelAsync();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await RunOperationAsync(_service.StopAsync);
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.Warning($"[RedStone] Operation failed: {exception.Message}");
            Notify(exception.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
            RefreshFromService();
        }
    }

    public void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(HongshiMultiplayerService.LogsDirectory);
            var startInfo = new ProcessStartInfo
            {
                FileName = HongshiMultiplayerService.LogsDirectory,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            Logger.Error($"[RedStone] Failed to open logs directory: {exception}");
            Notify(exception.Message, NotificationType.Error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _isActive = false;
        _service.StateChanged -= OnServiceStateChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private static void Notify(string message, NotificationType type)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
            window.Notice(message, type);
    }
}
