using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Components.Downloader;
using Portal.Bedrock.Standard.Interface;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
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
            sender is not Control { DataContext: BedrockGdkVersion version }) return;

        var folders = _viewModel.GetTraditionalInstallFolders();
        if (folders.Count == 0)
        {
            _viewModel.StatusText = "请先在设置中添加一个标准游戏目录。";
            return;
        }

        var selectedFolder = _viewModel.GetPreferredInstallFolder(folders);
        var folderSelector = new ComboBox
        {
            ItemsSource = folders,
            SelectedItem = selectedFolder,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        folderSelector.ItemTemplate = new FuncDataTemplate<MinecraftFolderEntry>((folder, _) => new TextBlock
        {
            Text = folder.FolderName,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var destinationText = new TextBlock
        {
            Text = _viewModel.GetDestinationPath(version, selectedFolder),
            TextWrapping = TextWrapping.Wrap
        };
        folderSelector.SelectionChanged += (_, _) =>
        {
            if (folderSelector.SelectedItem is MinecraftFolderEntry folder)
                destinationText.Text = _viewModel.GetDestinationPath(version, folder);
        };

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = _viewModel.GetInstallDetails(version, selectedFolder),
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock { Text = "安装目录" },
                folderSelector,
                destinationText
            }
        };

        var result = await OverlayDialog.ShowStandardAsync(content, null, this.GetTopLevel().TryGetHostId(), new OverlayDialogOptions
        {
            Title = $"安装 Minecraft 基岩版 {version.Id}",
            Buttons = DialogButton.YesNo,
            OverrideYesButtonText = "开始安装",
            OverrideNoButtonText = "取消",
            CanLightDismiss = false,
            CanResize = false
        });
        if (result == DialogResult.Yes && folderSelector.SelectedItem is MinecraftFolderEntry folder)
            _ = _viewModel.InstallAsync(version, folder);
    }
}

public partial class BedrockInstallationViewModel : ObservableObject
{
    private readonly List<BedrockGdkVersion> _allVersions = [];

    public ObservableCollection<BedrockGdkVersion> Versions { get; } = [];

    [ObservableProperty] public partial int SelectedReleaseChannel { get; set; } = 1;
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsInstalling { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "正在获取 GDK 版本列表...";

    public bool CanInstall => !IsInstalling && !IsLoading && BedrockInstallationService.DefaultInstaller is not null &&
                               GetTraditionalInstallFolders().Count > 0;
    partial void OnIsInstallingChanged(bool value) => UpdateInstallState();
    partial void OnIsLoadingChanged(bool value) => UpdateInstallState();
    partial void OnSelectedReleaseChannelChanged(int value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public async Task LoadVersionsAsync()
    {
        if (IsLoading || BedrockInstallationService.DefaultInstaller is not { } installer) return;

        IsLoading = true;
        var loaded = false;
        StatusText = "正在从版本源获取 GDK 版本列表...";
        try
        {
            var versions = await installer.GetGdkVersionsAsync(false, CancellationToken.None);
            _allVersions.Clear();
            _allVersions.AddRange(versions);
            loaded = true;
        }
        catch (Exception exception)
        {
            StatusText = $"无法获取 GDK 版本列表：{exception.Message}";
        }
        finally
        {
            IsLoading = false;
            if (loaded) ApplyFilter();
        }
    }

    public async Task InstallAsync(BedrockGdkVersion version, MinecraftFolderEntry folder)
    {
        if (!CanInstall || folder.DetectedLayout.Kind != MinecraftFolderKind.Standard ||
            BedrockInstallationService.DefaultInstaller is null) return;

        var installer = BedrockInstallationService.DefaultInstaller;
        var instanceName = version.Id;
        var destination = Path.Combine(folder.FolderPath, "bedrock_versions", instanceName);
        IsInstalling = true;

        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装 Minecraft 基岩版 {instanceName}",
            Description = "正在准备 GDK 安装包",
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
                Name = "下载并校验 GDK 安装包",
                Description = "正在连接下载服务器",
                Progress = 0
            }, async step =>
            {
                downloadContext = step;
                await downloadFinished.Task.WaitAsync(step.CancellationToken);
            });
            downloadStep.Start();
            TaskCompletionSource? extractionFinished = null;
            TaskExecutionContext? extractionContext = null;
            ManagedTask? extractionStep = null;

            var progress = new Progress<BedrockInstallProgress>(update =>
            {
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

                    if (update.State == "Extracting" && extractionStep is null)
                    {
                        downloadFinished.TrySetResult();
                        extractionFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        extractionStep = context.CreateChild(new TaskOptions
                        {
                            Name = "解压 GDK 安装包",
                            Description = "正在准备解压",
                            Progress = 0
                        }, async step =>
                        {
                            extractionContext = step;
                            await extractionFinished.Task.WaitAsync(step.CancellationToken);
                        });
                        extractionStep.Start();
                    }

                    var value = update.Total > 0 ? Math.Clamp((double)update.Current / update.Total, 0, 1) : (double?)null;
                    if (update.State == "Downloading" && downloadContext is { } downloading &&
                        !downloading.Task.IsTerminal && !downloading.Task.IsCancellationRequested)
                    {
                        downloading.ReportProgress(value);
                        downloading.SetDescription(FormatDownloadDescription(update, value));
                    }
                    else if (update.State == "Extracting" && extractionContext is { } extracting &&
                             !extracting.Task.IsTerminal && !extracting.Task.IsCancellationRequested)
                    {
                        extracting.ReportProgress(value);
                        extracting.SetDescription(string.IsNullOrWhiteSpace(update.Item)
                            ? "正在解压 GDK 安装包"
                            : $"正在解压 {update.Item}");
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
            });

            try
            {
                await installer.InstallGdkAsync(new BedrockOnlineInstallRequest(
                    version, destination, context.CancellationToken), progress);
                downloadFinished.TrySetResult();
                extractionFinished?.TrySetResult();
            }
            catch (Exception exception)
            {
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
        try
        {
            await task.Completion;
            if (task.Status == ManagedTaskStatus.Cancelled)
                throw new OperationCanceledException();
            if (task.Exception is not null)
                throw task.Exception;
            StatusText = $"{instanceName} 已安装完成。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "安装已取消。";
        }
        catch (Exception exception)
        {
            StatusText = $"安装失败：{exception.Message}";
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void UpdateInstallState()
    {
        if (BedrockInstallationService.DefaultInstaller is null)
            StatusText = "基岩版安装仅支持 Windows。";
        else if (GetTraditionalInstallFolders().Count == 0)
            StatusText = "请先在设置中添加一个标准游戏目录。";
        else if (!IsLoading && !IsInstalling && _allVersions.Count == 0)
            StatusText = "没有可用的 GDK 版本。";

        OnPropertyChanged(nameof(CanInstall));
    }

    private void ApplyFilter()
    {
        IEnumerable<BedrockGdkVersion> versions = _allVersions;
        versions = SelectedReleaseChannel switch
        {
            1 => versions.Where(version => !version.IsPreview),
            2 => versions.Where(version => version.IsPreview),
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
        return $"版本：{version.Id}\n渠道：{version.ChannelLabel}\n构建：GDK x64\n发布日期：{version.ReleaseTime:g}";
    }

    public string GetDestinationPath(BedrockGdkVersion version, MinecraftFolderEntry folder) =>
        Path.Combine(folder.FolderPath, "bedrock_versions", version.Id);

    public List<MinecraftFolderEntry> GetTraditionalInstallFolders() => Data.ConfigEntry.TraditionalMinecraftFolders.ToList();

    public MinecraftFolderEntry GetPreferredInstallFolder(IReadOnlyList<MinecraftFolderEntry> folders) =>
        Data.ConfigEntry.DefaultMinecraftFolder is { DetectedLayout.Kind: MinecraftFolderKind.Standard } folder &&
        folders.Contains(folder) ? folder : folders[0];

    private static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task> operation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var step = context.CreateChild(new TaskOptions { Name = name, Description = description, Progress = 0 }, operation);
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
}
