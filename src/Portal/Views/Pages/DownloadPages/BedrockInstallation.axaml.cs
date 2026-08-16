using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Components.Downloader;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class BedrockInstallation : UserControl
{
    private readonly BedrockInstallationViewModel _viewModel;

    public BedrockInstallation()
    {
        InitializeComponent();
        DataContext = _viewModel = new BedrockInstallationViewModel();
        Loaded += async (_, _) => await _viewModel.LoadVersionsAsync();
    }

    private async void VersionCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed ||
            sender is not Control { DataContext: BedrockVersion version }) return;

        var folders = _viewModel.GetTraditionalInstallFolders();
        if (folders.Count == 0)
        {
            _viewModel.StatusText = "请先在设置中添加一个 Portal 游戏目录。";
            return;
        }

        var result = await OverlayDialog.ShowCustomAsync<BedrockInstallDialog, BedrockInstallDialogViewModel,
            BedrockInstallDialogResult>(new BedrockInstallDialogViewModel(version, folders,
                _viewModel.GetPreferredInstallFolder(folders), _viewModel), this.GetTopLevel().TryGetHostId(),
            new OverlayDialogOptions
            {
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                IsCloseButtonVisible = false
            });
        if (result is not null) _ = _viewModel.InstallAsync(version, result.Folder);
    }
}

public partial class BedrockInstallationViewModel : ObservableObject, IDisposable
{
    private readonly List<BedrockVersion> _allVersions = [];
    private readonly CancellationTokenSource _pageCancellation = new();
    private bool _disposed;

    public ObservableCollection<BedrockVersion> Versions { get; } = [];

    [ObservableProperty] public partial int SelectedReleaseChannel { get; set; } = 1;
    [ObservableProperty] public partial int SelectedBuildType { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsInstalling { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "正在获取基岩版版本列表...";

    public bool CanInstall => !IsInstalling && !IsLoading && BedrockInstallationService.DefaultInstaller is not null &&
                              GetTraditionalInstallFolders().Count > 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pageCancellation.Cancel();
        Versions.Clear();
        _allVersions.Clear();
    }

    partial void OnIsInstallingChanged(bool value)
    {
        UpdateInstallState();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        UpdateInstallState();
    }

    partial void OnSelectedReleaseChannelChanged(int value)
    {
        ApplyFilter();
    }

    partial void OnSelectedBuildTypeChanged(int value)
    {
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    public async Task LoadVersionsAsync()
    {
        if (IsLoading || BedrockInstallationService.DefaultInstaller is not { } installer) return;

        IsLoading = true;
        var loaded = false;
        StatusText = "正在从版本源获取基岩版版本列表...";
        try
        {
            var versions = await installer.GetVersionsAsync(false, _pageCancellation.Token);
            if (_disposed) return;
            _allVersions.Clear();
            _allVersions.AddRange(versions);
            loaded = true;
        }
        catch (OperationCanceledException) when (_pageCancellation.IsCancellationRequested)
        {
            Logger.Debug("[BedrockInstall] Version list request cancelled because the page closed.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            StatusText = $"无法获取基岩版版本列表：{exception.Message}";
        }
        finally
        {
            IsLoading = false;
            if (loaded) ApplyFilter();
        }
    }

    public async Task InstallAsync(BedrockVersion version, MinecraftFolderEntry folder)
    {
        if (!CanInstall ||
            folder.DetectedLayout.Kind is not (MinecraftFolderKind.Standard or MinecraftFolderKind.PortalMc) ||
            BedrockInstallationService.DefaultInstaller is null) return;

        var installer = BedrockInstallationService.DefaultInstaller;
        var instanceName = GetInstanceName(version);
        var buildLabel = version.BuildLabel;
        var destination = Path.Combine(folder.FolderPath,
            folder.DetectedLayout.Kind == MinecraftFolderKind.PortalMc ? "bedrock_instances" : "bedrock_versions",
            instanceName);
        Logger.Info($"[BedrockInstall] Queuing {buildLabel} {instanceName} installation to {destination}.");
        IsInstalling = true;

        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装 Minecraft 基岩版 {instanceName}",
            Description = $"正在准备 {buildLabel} 安装包",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消安装",
                    Description = "取消当前安装任务。",
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
            context.CancellationToken.ThrowIfCancellationRequested();
            await RunStepAsync(context, "准备安装", "正在检查安装目录", step =>
            {
                if (Directory.Exists(destination))
                    throw new InvalidOperationException("目标实例已存在，请更换实例名称。");
                step.ReportProgress(1);
                return Task.CompletedTask;
            });

            var downloadFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskExecutionContext? downloadContext = null;
            var downloadStep = context.CreateChild(new TaskOptions
            {
                Name = $"下载并校验 {buildLabel} 安装包",
                Description = "正在连接下载服务器",
                Progress = 0
            }, async step =>
            {
                downloadContext = step;
                await downloadFinished.Task.WaitAsync(step.CancellationToken);
            });
            downloadStep.Start();
            TaskCompletionSource? extractionFinished = null;
            ManagedTask? extractionStep = null;

            var progress = new ThrottledProgress<BedrockInstallProgress>(update =>
            {
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (update.State == "Extracting" && extractionStep is null)
                {
                    downloadFinished.TrySetResult();
                    extractionFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    extractionStep = context.CreateChild(new TaskOptions
                    {
                        Name = $"解压 {buildLabel} 安装包",
                        Description = "正在解压",
                        Progress = 0
                    }, async step => { await extractionFinished.Task.WaitAsync(step.CancellationToken); });
                    extractionStep.Start();
                }

                var value = update.Total > 0 ? Math.Clamp((double)update.Current / update.Total, 0, 1) : (double?)null;
                if (update.State == "Downloading" && downloadContext is { } downloading &&
                    !downloading.Task.IsTerminal && !downloading.Task.IsCancellationRequested)
                {
                    downloading.ReportProgress(value);
                    downloading.SetDescription(FormatDownloadDescription(update, value));
                }
                else if (downloadContext is { } downloadingState && !downloadingState.Task.IsTerminal &&
                         !downloadingState.Task.IsCancellationRequested)
                {
                    downloadingState.SetDescription(update.State switch
                    {
                        "Selecting source" => "正在测速并选择最快下载源",
                        "Using cached package" => "正在校验并使用本地安装包缓存",
                        _ => $"安装状态：{update.State}"
                    });
                }
            });

            try
            {
                await Task.Run(() => installer.InstallAsync(new BedrockInstallRequest(
                    version, destination, context.CancellationToken), progress), context.CancellationToken);
                downloadFinished.TrySetResult();
                extractionFinished?.TrySetResult();
            }
            catch (Exception exception)
            {
                await DeleteDirectoryAsync(destination);

                if (exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
                {
                    downloadFinished.TrySetCanceled(context.CancellationToken);
                    extractionFinished?.TrySetCanceled(context.CancellationToken);
                }
                else
                {
                    downloadFinished.TrySetException(exception);
                    extractionFinished?.TrySetException(exception);
                }

                throw;
            }
            finally
            {
                if (!downloadStep.IsTerminal) await downloadStep.Completion;
                if (extractionStep is not null && !extractionStep.IsTerminal) await extractionStep.Completion;
            }

            await RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的新实例", step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription($"已完成 Minecraft 基岩版 {instanceName} 的安装");
        });

        task.Start();
        _ = ObserveInstallationAsync(task, new WeakReference<BedrockInstallationViewModel>(this));
        await Task.CompletedTask;
    }

    private static async Task ObserveInstallationAsync(ManagedTask task,
        WeakReference<BedrockInstallationViewModel> viewModelReference)
    {
        try
        {
            await task.Completion;
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[BedrockInstall] Installation cancelled: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }

        if (viewModelReference.TryGetTarget(out var viewModel) && !viewModel._disposed)
            viewModel.IsInstalling = false;
    }

    private void UpdateInstallState()
    {
        if (BedrockInstallationService.DefaultInstaller is null)
            StatusText = "基岩版安装仅支持 Windows。";
        else if (GetTraditionalInstallFolders().Count == 0)
            StatusText = "请先在设置中添加一个 Portal 游戏目录。";
        else if (!IsLoading && !IsInstalling && _allVersions.Count == 0)
            StatusText = "没有可用的基岩版版本。";

        OnPropertyChanged(nameof(CanInstall));
    }

    private void ApplyFilter()
    {
        IEnumerable<BedrockVersion> versions = _allVersions;
        versions = SelectedReleaseChannel switch
        {
            1 => versions.Where(version => !version.IsPreview),
            2 => versions.Where(version => version.IsPreview),
            _ => versions
        };
        versions = SelectedBuildType switch
        {
            1 => versions.Where(version => version.BuildType == BedrockBuildType.UWP),
            2 => versions.Where(version => version.BuildType == BedrockBuildType.GDK),
            _ => versions
        };
        if (!string.IsNullOrWhiteSpace(SearchText))
            versions = versions.Where(version => version.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        Versions.Clear();
        foreach (var version in versions) Versions.Add(version);
        if (!IsLoading && !IsInstalling)
            StatusText = $"共 {Versions.Count} 个版本";
    }

    public string GetInstallDetails(BedrockGdkVersion version, MinecraftFolderEntry folder)
    {
        return $"版本：{version.Id}\n渠道：{version.ChannelLabel}\n构建：{version.BuildLabel} x64\n发布日期：{version.ReleaseTime:g}";
    }

    public string GetDestinationPath(BedrockGdkVersion version, MinecraftFolderEntry folder)
    {
        return Path.Combine(folder.FolderPath,
            folder.DetectedLayout.Kind == MinecraftFolderKind.PortalMc ? "bedrock_instances" : "bedrock_versions",
            GetInstanceName(version));
    }

    private static string GetInstanceName(BedrockGdkVersion version)
    {
        return version is BedrockVersion { BuildType: BedrockBuildType.UWP }
            ? $"{version.Id}-UWP"
            : version.Id;
    }

    public List<MinecraftFolderEntry> GetTraditionalInstallFolders()
    {
        return Data.ConfigEntry.InstallableMinecraftFolders.ToList();
    }

    public MinecraftFolderEntry GetPreferredInstallFolder(IReadOnlyList<MinecraftFolderEntry> folders)
    {
        return Data.ConfigEntry.DefaultMinecraftFolder is { SupportsInstallation: true } folder &&
               folders.Contains(folder)
            ? folder
            : folders[0];
    }

    private static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task> operation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var step = context.CreateChild(new TaskOptions { Name = name, Description = description, Progress = 0 },
            operation);
        step.Start();
        await step.Completion;
        if (step.Exception is not null) throw new InvalidOperationException(step.Exception.Message, step.Exception);
        context.CancellationToken.ThrowIfCancellationRequested();
    }

    private static string FormatDownloadDescription(BedrockInstallProgress update, double? progress)
    {
        var percentage = progress is { } value ? $" ({value:P0})" : string.Empty;
        var speed = update.Speed > 0 ? $"，{DefaultDownloader.FormatSize(update.Speed, true)}" : string.Empty;
        var remaining = update.EstimatedRemaining is { } eta && eta > TimeSpan.Zero
            ? $"，剩余约 {eta:mm\\:ss}"
            : string.Empty;
        return $"正在下载 {update.Item}{percentage}{speed}{remaining}";
    }

    private static Task DeleteDirectoryAsync(string directory)
    {
        return Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception exception)
            {
                Logger.Warning($"[BedrockInstall] Failed to clean up {directory}: {exception}");
            }
        });
    }

    private sealed class ThrottledProgress<T>(Action<T> handler) : IProgress<T>
    {
        private readonly Lock _lock = new();
        private T? _latest;
        private bool _scheduled;

        public void Report(T value)
        {
            lock (_lock)
            {
                _latest = value;
                if (_scheduled) return;
                _scheduled = true;
            }

            Dispatcher.UIThread.Post(Dispatch, DispatcherPriority.Background);
        }

        private void Dispatch()
        {
            T? latest;
            lock (_lock)
            {
                latest = _latest;
                _latest = default;
                _scheduled = false;
            }

            if (latest is not null) handler(latest);
        }
    }
}