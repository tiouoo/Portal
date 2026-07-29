using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using Velopack;
using Velopack.Sources;

namespace Portal.Module.Update;

public sealed partial class UpdateApp : ObservableObject
{
    private const string RepositoryUrl = "https://github.com/tiouoo/Portal";
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private UpdateManager? _manager;
    private UpdateInfo? _update;
    private string? _channel;
    private int _isApplying;

    private UpdateApp()
    {
    }

    public static UpdateApp Instance { get; } = new();

    [ObservableProperty] public partial UpdateState State { get; private set; } = UpdateState.Idle;
    [ObservableProperty] public partial string? Version { get; private set; }
    [ObservableProperty] public partial string? ErrorMessage { get; private set; }

    public bool HasUpdate => State is UpdateState.DownloadingDelta or UpdateState.ReadyToRestart or UpdateState.ManualDownloadRequired;
    public string ActionText => State switch
    {
        UpdateState.DownloadingDelta => "增量更新包正在下载中",
        UpdateState.ReadyToRestart => "重启更新",
        UpdateState.ManualDownloadRequired => "手动下载",
        _ => "下载新版本"
    };
    public string TitleText => State == UpdateState.ReadyToRestart ? "重启更新" : "发现新版本";
    public bool CanChangeChannel => State is not (UpdateState.Checking or UpdateState.DownloadingDelta or UpdateState.ReadyToRestart);

    partial void OnStateChanged(UpdateState value)
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(CanChangeChannel));
        Data.UiProperty.FoundNewVersion = HasUpdate;
        Data.UiProperty.IsLatestVersion = value == UpdateState.Latest;
    }

    partial void OnVersionChanged(string? value) => Data.UiProperty.NewVersion = value ?? string.Empty;

    public async Task CheckAndDownloadAsync(TopLevel? sender = null, bool silent = false)
    {
        await _operationLock.WaitAsync();
        var previousState = State;
        var previousVersion = Version;
        try
        {
            if (State == UpdateState.DownloadingDelta || State == UpdateState.ReadyToRestart) return;

            State = UpdateState.Checking;
            ErrorMessage = null;
            _channel = NormalizeChannel();
            _manager = CreateManager(_channel);
            if (!_manager.IsInstalled)
            {
                SetManualDownload("当前版本不是 Velopack 安装版本，请重新下载安装最新版。");
                return;
            }

            _update = await _manager.CheckForUpdatesAsync();
            if (_update is null)
            {
                Version = null;
                State = UpdateState.Latest;
                if (!silent && sender is not null) sender.Notice("当前是最新版本", NotificationType.Success);
                return;
            }

            Version = _update.TargetFullRelease.Version.ToString();
            if (_update.IsDowngrade || _update.DeltasToTarget.Length == 0)
            {
                SetManualDownload("当前版本没有可用的增量更新包。");
                return;
            }

            State = UpdateState.DownloadingDelta;
            _ = DownloadDeltaAsync(_manager, _update);
        }
        catch (Exception ex)
        {
            Logger.Error($"检查更新失败：{ex}");
            State = previousState is UpdateState.ManualDownloadRequired or UpdateState.ReadyToRestart
                ? previousState
                : UpdateState.Idle;
            Version = previousVersion;
            ErrorMessage = ex.Message;
            if (!silent && sender is not null)
                sender.Notice($"检查更新失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task HandleActionAsync(TopLevel topLevel)
    {
        switch (State)
        {
            case UpdateState.DownloadingDelta:
                topLevel.Notice("增量更新包正在下载中", NotificationType.Information);
                break;
            case UpdateState.ReadyToRestart:
                await ApplyAsync();
                break;
            case UpdateState.ManualDownloadRequired:
                await topLevel.Launcher.LaunchUriAsync(new Uri(GetDownloadUrl()));
                break;
        }
    }

    public async Task ApplyAsync()
    {
        if (State != UpdateState.ReadyToRestart || _manager is null || _update is null) return;
        if (Interlocked.Exchange(ref _isApplying, 1) != 0) return;
        if (!await ApplicationEvents.RaiseAppExiting())
        {
            Interlocked.Exchange(ref _isApplying, 0);
            return;
        }

        App.Method.FlushConfig();
        _manager.ApplyUpdatesAndRestart(_update);
    }

    public string GetDownloadUrl() => $"{RepositoryUrl}/releases/tag/publish-{_channel ?? NormalizeChannel()}";

    private async Task DownloadDeltaAsync(UpdateManager manager, UpdateInfo update)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = "下载 Portal 增量更新",
            Description = $"正在下载：{update.TargetFullRelease.Version}",
            Progress = 0
        }, async context =>
        {
            context.SetRunning("增量更新包正在下载中");
            await manager.DownloadUpdatesAsync(update,
                progress => Dispatcher.UIThread.Post(() => context.ReportProgress(progress / 100d)),
                context.CancellationToken);
            context.SetDescription("增量更新包下载完成");
        });

        task.Start();
        await task.Completion;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (task.Status == ManagedTaskStatus.Completed && manager.UpdatePendingRestart is not null)
            {
                State = UpdateState.ReadyToRestart;
                ErrorMessage = null;
                return;
            }

            var error = task.Exception?.Message ?? "增量更新包下载失败。";
            Logger.Error($"增量更新失败：{error}");
            SetManualDownload(error);
        });
    }

    private static UpdateManager CreateManager(string channel)
    {
        var source = new GithubSource(RepositoryUrl, null, channel == "commit");
        return new UpdateManager(new DeltaOnlyUpdateSource(source), new UpdateOptions
        {
            ExplicitChannel = GetVelopackChannel(channel),
            MaximumDeltasBeforeFallback = int.MaxValue
        });
    }

    private void SetManualDownload(string error)
    {
        ErrorMessage = error;
        State = UpdateState.ManualDownloadRequired;
    }

    private static string GetVelopackChannel(string channel)
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" :
            throw new PlatformNotSupportedException("当前操作系统不支持自动更新。");
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => throw new PlatformNotSupportedException("当前处理器架构不支持自动更新。")
        };
        return $"{os}-{arch}-{channel}";
    }

    private static string NormalizeChannel() => Data.UiProperty.OverrideUpdateChannel.Trim().ToLowerInvariant() switch
    {
        "nightly" => "nightly",
        "commit" => "commit",
        _ => throw new NotSupportedException($"不支持更新通道“{Data.UiProperty.OverrideUpdateChannel}”。")
    };
}
