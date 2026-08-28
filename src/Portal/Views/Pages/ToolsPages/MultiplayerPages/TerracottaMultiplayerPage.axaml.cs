using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Const;
using Portal.Core.Module.Initialize;
using Portal.Core.Module.Multiplayer;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Views.Pages.ToolsPages.MultiplayerPages;

public partial class TerracottaMultiplayerPage : UserControl
{
    private readonly TerracottaMultiplayerViewModel _viewModel;

    public TerracottaMultiplayerPage()
    {
        InitializeComponent();
        _viewModel = new TerracottaMultiplayerViewModel();
        DataContext = _viewModel;
    }

    public TerracottaMultiplayerViewModel ViewModel => _viewModel;

    private async void PasteJoinCode_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        _viewModel.JoinCode = await clipboard.TryGetTextAsync() ?? string.Empty;
    }

    private async void CopyRoomCode_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (string.IsNullOrWhiteSpace(_viewModel.RoomCodeOrAddress)) return;
        await clipboard.SetTextAsync(_viewModel.RoomCodeOrAddress);
        TopLevel.GetTopLevel(this)?.Notice(
            CommonLanguageManager.Instance.multiplayer_roomCodeCopied.CurrentValue(), NotificationType.Success);
    }

    private async void ExportReport_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = CommonLanguageManager.Instance.multiplayer_terracottaExportReport.CurrentValue(),
            SuggestedFileName = "Portal-terracotta-diagnostics.txt",
            FileTypeChoices =
            [
                new FilePickerFileType(CommonLanguageManager.Instance.multiplayer_terracottaExportReport.CurrentValue())
                {
                    Patterns = ["*.txt"]
                }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            await File.WriteAllTextAsync(path, _viewModel.GetDiagnosticReport());
            topLevel.Notice(CommonLanguageManager.Instance.multiplayer_terracottaExportReport.CurrentValue(),
                NotificationType.Success);
        }
        catch (Exception exception)
        {
            Logger.Error($"[Terracotta] Failed to export report: {exception}");
            topLevel.Notice(exception.Message, NotificationType.Error);
        }
    }
}

public partial class TerracottaMultiplayerViewModel : ObservableObject, IAsyncDisposable, IMultiplayerPageLifecycle
{
    private const string RoomCodePattern = @"^U\/[A-Z0-9]{4}(?:-[A-Z0-9]{4}){3}$";
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TerracottaMultiplayerService _service = TerracottaMultiplayerService.Instance;
    private bool _disposed;
    private bool _isActive;

    public TerracottaMultiplayerViewModel()
    {
        _service.StateChanged += OnServiceStateChanged;
        PlayerName = string.IsNullOrWhiteSpace(Data.ConfigEntry.OnlinePlayerName)
            ? Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Name ?? string.Empty
            : Data.ConfigEntry.OnlinePlayerName;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadCard))]
    [NotifyPropertyChangedFor(nameof(ShowDownloadBanner))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(DownloadProgress))]
    [NotifyPropertyChangedFor(nameof(DownloadStageText))]
    [NotifyPropertyChangedFor(nameof(DownloadHintText))]
    [NotifyPropertyChangedFor(nameof(DownloadActionText))]
    [NotifyPropertyChangedFor(nameof(ShowStarting))]
    [NotifyPropertyChangedFor(nameof(ShowSessionForm))]
    [NotifyPropertyChangedFor(nameof(ShowInProgress))]
    [NotifyPropertyChangedFor(nameof(ShowReady))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowNotRunning))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsHostSession))]
    [NotifyPropertyChangedFor(nameof(PlayerCountText))]
    [NotifyPropertyChangedFor(nameof(HasNoPlayers))]
    [NotifyPropertyChangedFor(nameof(ShowRoomCodeCard))]
    [NotifyPropertyChangedFor(nameof(RoomCodeOrAddress))]
    [NotifyPropertyChangedFor(nameof(RoomCodeLabelText))]
    [NotifyPropertyChangedFor(nameof(ErrorTypeText))]
    [NotifyPropertyChangedFor(nameof(ErrorMessageText))]
    [NotifyPropertyChangedFor(nameof(SessionDescription))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateBanner))]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    [NotifyPropertyChangedFor(nameof(InstalledVersionText))]
    private partial TerracottaState? State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitHost))]
    [NotifyPropertyChangedFor(nameof(CanSubmitJoin))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionDescription))]
    [NotifyPropertyChangedFor(nameof(CanSubmitHost))]
    [NotifyPropertyChangedFor(nameof(ShowRoomCodeError))]
    public partial bool IsHostTabSelected { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionDescription))]
    [NotifyPropertyChangedFor(nameof(CanSubmitJoin))]
    [NotifyPropertyChangedFor(nameof(ShowRoomCodeError))]
    public partial bool IsJoinTabSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitHost))]
    [NotifyPropertyChangedFor(nameof(CanSubmitJoin))]
    public partial string PlayerName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitJoin))]
    [NotifyPropertyChangedFor(nameof(ShowRoomCodeError))]
    public partial string JoinCode { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadActionText))]
    [NotifyPropertyChangedFor(nameof(DownloadHintText))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateBanner))]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    public partial bool HasUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    public partial string? LatestVersion { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRoomCodeError))]
    public partial bool IsRoomCodeTouched { get; set; }

    public bool ShowDownloadCard => IsDownloading || ShowDownloadBanner;
    public bool ShowDownloadBanner => State is { BinaryInstalled: false } &&
                                      State.Status != TerracottaMultiplayerStatus.Downloading;
    public bool IsDownloading => State?.Status == TerracottaMultiplayerStatus.Downloading;
    public int DownloadProgress => State?.DownloadProgress ?? 0;
    public bool ShowStarting => State?.Status == TerracottaMultiplayerStatus.Starting;
    public bool ShowSessionForm => State is { HttpPort: not null } &&
                                   State.Status is TerracottaMultiplayerStatus.Idle or TerracottaMultiplayerStatus.Waiting;
    public bool ShowInProgress => State?.Status is TerracottaMultiplayerStatus.HostScanning
        or TerracottaMultiplayerStatus.HostStarting or TerracottaMultiplayerStatus.GuestConnecting
        or TerracottaMultiplayerStatus.GuestStarting;
    public bool ShowReady => State?.Status is TerracottaMultiplayerStatus.HostReady
        or TerracottaMultiplayerStatus.GuestReady;
    public bool ShowError => State?.Status is TerracottaMultiplayerStatus.Error or TerracottaMultiplayerStatus.Fatal;
    public bool ShowNotRunning => State is { BinaryInstalled: true, HttpPort: null } &&
                                  State.Status == TerracottaMultiplayerStatus.Idle;

    public bool IsHostSession => State?.Status == TerracottaMultiplayerStatus.HostReady;

    public string StatusText => State is null
        ? string.Empty
        : State.Status switch
        {
            TerracottaMultiplayerStatus.Idle => CommonLanguageManager.Instance.multiplayer_terracottaStatusIdle.CurrentValue(),
            TerracottaMultiplayerStatus.Starting => CommonLanguageManager.Instance.multiplayer_terracottaStatusStarting.CurrentValue(),
            TerracottaMultiplayerStatus.Downloading => CommonLanguageManager.Instance.multiplayer_terracottaStatusDownloading.CurrentValue(),
            TerracottaMultiplayerStatus.Waiting => CommonLanguageManager.Instance.multiplayer_terracottaStatusWaiting.CurrentValue(),
            TerracottaMultiplayerStatus.HostScanning => CommonLanguageManager.Instance.multiplayer_terracottaStatusHostScanning.CurrentValue(),
            TerracottaMultiplayerStatus.HostStarting => CommonLanguageManager.Instance.multiplayer_terracottaStatusHostStarting.CurrentValue(),
            TerracottaMultiplayerStatus.HostReady => CommonLanguageManager.Instance.multiplayer_terracottaStatusHostReady.CurrentValue(),
            TerracottaMultiplayerStatus.GuestConnecting => CommonLanguageManager.Instance.multiplayer_terracottaStatusGuestConnecting.CurrentValue(),
            TerracottaMultiplayerStatus.GuestStarting => CommonLanguageManager.Instance.multiplayer_terracottaStatusGuestStarting.CurrentValue(),
            TerracottaMultiplayerStatus.GuestReady => CommonLanguageManager.Instance.multiplayer_terracottaStatusGuestReady.CurrentValue(),
            TerracottaMultiplayerStatus.Error => CommonLanguageManager.Instance.multiplayer_terracottaStatusError.CurrentValue(),
            TerracottaMultiplayerStatus.Fatal => CommonLanguageManager.Instance.multiplayer_terracottaStatusFatal.CurrentValue(),
            _ => string.Empty
        };

    public string DownloadStageText => State?.DownloadStage switch
    {
        TerracottaDownloadStage.Downloading => CommonLanguageManager.Instance.multiplayer_terracottaDownloadProgress.CurrentValue(),
        TerracottaDownloadStage.Verifying => CommonLanguageManager.Instance.multiplayer_terracottaVerifying.CurrentValue(),
        TerracottaDownloadStage.Extracting => CommonLanguageManager.Instance.multiplayer_terracottaExtracting.CurrentValue(),
        TerracottaDownloadStage.Installing => CommonLanguageManager.Instance.multiplayer_terracottaInstalling.CurrentValue(),
        _ => CommonLanguageManager.Instance.multiplayer_terracottaConnecting.CurrentValue()
    };

    public string DownloadActionText => State is { BinaryInstalled: false }
        ? CommonLanguageManager.Instance.multiplayer_terracottaDownloadCore.CurrentValue()
        : HasUpdate
            ? CommonLanguageManager.Instance.multiplayer_terracottaUpdateCore.CurrentValue()
            : CommonLanguageManager.Instance.multiplayer_terracottaDownloadCore.CurrentValue();

    public string DownloadHintText => State is { BinaryInstalled: false }
        ? CommonLanguageManager.Instance.multiplayer_terracottaNotRunning.CurrentValue()
        : HasUpdate
            ? CommonLanguageManager.Instance.multiplayer_terracottaUpdateAvailable.CurrentValue()
            : CommonLanguageManager.Instance.multiplayer_terracottaNotRunning.CurrentValue();

    public string PlayerCountText => string.Format(
        CommonLanguageManager.Instance.multiplayer_terracottaPlayersInRoom.CurrentValue(),
        Players.Count);

    public bool HasNoPlayers => Players.Count == 0;

    public bool ShowRoomCodeCard => IsHostSession
        ? !string.IsNullOrWhiteSpace(RoomCodeOrAddress)
        : !string.IsNullOrWhiteSpace(RoomCodeOrAddress);

    public string RoomCodeLabelText => IsHostSession
        ? CommonLanguageManager.Instance.multiplayer_terracottaRoomCode.CurrentValue()
        : CommonLanguageManager.Instance.multiplayer_terracottaServerAddress.CurrentValue();

    public string RoomCodeOrAddress => IsHostSession
        ? State?.RoomCode ?? string.Empty
        : State?.ServerPort is { } port ? $"127.0.0.1:{port}" : string.Empty;

    public string ErrorTypeText => (State?.ErrorType) switch
    {
        TerracottaErrorType.Network => CommonLanguageManager.Instance.multiplayer_terracottaErrorNetwork.CurrentValue(),
        TerracottaErrorType.Install => CommonLanguageManager.Instance.multiplayer_terracottaErrorInstall.CurrentValue(),
        TerracottaErrorType.Terracotta => CommonLanguageManager.Instance.multiplayer_terracottaErrorTerracotta.CurrentValue(),
        TerracottaErrorType.Os => CommonLanguageManager.Instance.multiplayer_terracottaErrorOs.CurrentValue(),
        _ => CommonLanguageManager.Instance.multiplayer_terracottaErrorUnknown.CurrentValue()
    };

    public string ErrorMessageText => !string.IsNullOrWhiteSpace(State?.ErrorMessage)
        ? State.ErrorMessage
        : CommonLanguageManager.Instance.multiplayer_terracottaCheckNetwork.CurrentValue();

    public string SessionDescription => IsHostTabSelected
        ? CommonLanguageManager.Instance.multiplayer_terracottaHostDescription.CurrentValue()
        : CommonLanguageManager.Instance.multiplayer_terracottaJoinDescription.CurrentValue();

    public string StartHintText => CommonLanguageManager.Instance.multiplayer_terracottaStartDescription.CurrentValue();

    public bool ShowUpdateBanner => HasUpdate && State is { BinaryInstalled: true, HttpPort: null };
    public string UpdateText => string.Format(
        CommonLanguageManager.Instance.multiplayer_terracottaUpdateAvailable.CurrentValue(), LatestVersion);
    public string InstalledVersionText => string.Format(
        CommonLanguageManager.Instance.multiplayer_terracottaInstalledVersion.CurrentValue(),
        State?.InstalledVersion ?? string.Empty);

    public bool CanSubmitHost => !IsBusy && !string.IsNullOrWhiteSpace(PlayerName);
    public bool CanSubmitJoin => !IsBusy && !string.IsNullOrWhiteSpace(PlayerName) && IsRoomCodeValid;
    public bool IsRoomCodeValid => Regex.IsMatch(JoinCode.Trim(), RoomCodePattern, RegexOptions.IgnoreCase);
    public bool ShowRoomCodeError => IsJoinTabSelected && IsRoomCodeTouched && !IsRoomCodeValid;

    public ObservableCollection<TerracottaPlayer> Players { get; } = [];

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
        _ = AutoStartServiceAsync();
        _ = CheckForUpdateAsync();
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    private void RefreshFromService()
    {
        State = _service.GetState();
        Players.Clear();
        if (State.Players is { } players)
            foreach (var player in players)
                Players.Add(player);
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var update = await _service.CheckForUpdateAsync(_lifetime.Token);
            Dispatcher.UIThread.Post(() =>
            {
                HasUpdate = update.UpdateAvailable;
                LatestVersion = update.LatestVersion;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Logger.Debug($"[Terracotta] Update check failed: {exception.Message}");
        }
    }

    [RelayCommand]
    private void ClearJoinCode()
    {
        JoinCode = string.Empty;
        IsRoomCodeTouched = false;
    }

    [RelayCommand]
    private async Task DownloadCoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        var taskName = CommonLanguageManager.Instance.multiplayer_terracottaDownloadCore.CurrentValue();
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
            var progress = new Progress<TerracottaDownloadProgress>(item =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                    if (item.Fraction is { } fraction) context.ReportProgress(fraction);
                    if (item.Message is not null) context.SetDescription(item.Message);
                });
            });
            await _service.DownloadAsync(null, progress, context.CancellationToken);
            context.ReportProgress(1);
            context.SetDescription(CommonLanguageManager.Instance.multiplayer_componentDownloaded.CurrentValue());
        });
        task.Start();
        _ = ObserveInstallationAsync(task);
    }

    [RelayCommand]
    private async Task UpdateCoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        var taskName = CommonLanguageManager.Instance.multiplayer_terracottaUpdateCore.CurrentValue();
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
            var progress = new Progress<TerracottaDownloadProgress>(item =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                    if (item.Fraction is { } fraction) context.ReportProgress(fraction);
                    if (item.Message is not null) context.SetDescription(item.Message);
                });
            });
            var update = await _service.CheckForUpdateAsync(context.CancellationToken);
            if (update.UpdateAvailable)
                await _service.DownloadAsync(update.LatestVersion, progress, context.CancellationToken);
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
            Logger.Error($"[Terracotta] Installation task failed: {exception}");
        }
        finally
        {
            IsBusy = false;
            if (task.Status == ManagedTaskStatus.Completed)
            {
                RefreshFromService();
                _ = AutoStartServiceAsync();
                _ = CheckForUpdateAsync();
            }
        }
    }

    private async Task AutoStartServiceAsync()
    {
        if (!_isActive || IsBusy || _service.IsRunning || !_service.IsBinaryInstalled()) return;
        IsBusy = true;
        try
        {
            await _service.StartAsync(false, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Terracotta] Automatic service start failed: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
            RefreshFromService();
        }
    }

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _service.StartAsync(true, _lifetime.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !_lifetime.IsCancellationRequested)
        {
            Logger.Warning($"[Terracotta] Start failed: {exception.Message}");
            Notify(exception.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
            RefreshFromService();
        }
    }

    [RelayCommand]
    private async Task HostAsync()
    {
        if (!CanSubmitHost) return;
        IsHostTabSelected = true;
        IsJoinTabSelected = false;
        await RunRoomOperationAsync(NotificationType.Success);
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        if (!CanSubmitJoin) return;
        IsHostTabSelected = false;
        IsJoinTabSelected = true;
        await RunRoomOperationAsync(NotificationType.Success);
    }

    private async Task RunRoomOperationAsync(NotificationType successType)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (IsHostTabSelected)
                await _service.HostAsync(PlayerName, null, _lifetime.Token);
            else
                await _service.JoinAsync(PlayerName, JoinCode, _lifetime.Token);
            Notify(CommonLanguageManager.Instance.multiplayer_roomCreated.CurrentValue(), successType);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !_lifetime.IsCancellationRequested)
        {
            Logger.Warning($"[Terracotta] Room operation failed: {exception.Message}");
            Notify(exception.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
            RefreshFromService();
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _service.ResetStateAsync(_lifetime.Token);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Terracotta] Disconnect failed: {exception.Message}");
            Notify(exception.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
            RefreshFromService();
        }
    }

    [RelayCommand]
    private Task BackAsync() => DisconnectAsync();


    public string GetDiagnosticReport() => _service.GetDiagnosticReport();

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
