using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Components.Downloader;
using Portal.Const;
using Portal.Core.Const;
using Portal.Core.Operations.Java;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class JavaDownloadPage : UserControl
{
    public JavaDownloadPage()
    {
        InitializeComponent();
        DataContext = new JavaDownloadPageViewModel();
    }

    private async void DistributionCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: JavaDistributionItem item } ||
            DataContext is not JavaDownloadPageViewModel)
            return;

        var hostId = this.GetTopLevel().TryGetHostId();
        var selected = await OverlayDialog.ShowCustomAsync<JavaVersionDialog, JavaVersionDialogViewModel, JavaDistributionVersion>(
            new JavaVersionDialogViewModel(item), hostId,
            new OverlayDialogOptions
            {
                Title = $"选择 {item.DisplayName} 版本", Buttons = DialogButton.None,
                CanLightDismiss = false, CanResize = false
            });
        if (selected is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        var expectedFolder = Path.Combine(ConfigPath.JavaRuntimesPath, $"{selected.Vendor}-{selected.MajorVersion}");
        var duplicate = Data.ConfigEntry.JavaRuntimes.Any(x => x.MajorVersion == selected.MajorVersion &&
            x.JavaPath.StartsWith(expectedFolder, StringComparison.OrdinalIgnoreCase));
        if (duplicate && topLevel is not null)
        {
            var result = await OverlayDialog.ShowStandardAsync(new TextBlock
            {
                Margin = new Avalonia.Thickness(24),
                Text = $"{selected.Vendor} Java {selected.MajorVersion} 已安装，是否再次安装？",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }, null, hostId, new OverlayDialogOptions
            {
                Title = "Java 已安装", Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "再次安装", OverrideNoButtonText = "取消",
                CanLightDismiss = false, CanResize = false
            });
            if (result != DialogResult.Yes) return;
        }
        StartInstall(selected, topLevel);
    }

    private static void StartInstall(JavaDistributionVersion version, TopLevel? topLevel)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装 Java {version.Vendor} {version.MajorVersion}",
            Description = "正在准备下载",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消安装", Description = "取消当前 Java 安装", IconKey = "Cancel",
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
            context.SetRunning("正在下载 Java");
            var runtime = await JavaDistributionService.InstallAsync(version, ConfigPath.JavaRuntimesPath,
                ConfigPath.TempFolderPath, progress => ReportInstallProgress(context, progress), context.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Data.ConfigEntry.JavaRuntimes.Contains(runtime)) Data.ConfigEntry.JavaRuntimes.Add(runtime);
                Data.ConfigEntry.DefaultJavaRuntime ??= runtime;
            });
            context.ReportProgress(1);
            context.SetDescription($"Java {version.MajorVersion} 已安装");
        });
        task.Start();
        _ = ObserveInstallAsync(task, topLevel, version);
    }

    private static void ReportInstallProgress(TaskExecutionContext context, JavaInstallProgress progress)
    {
        if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
            try
            {
                context.ReportProgress(progress.Fraction);
                context.SetDescription(progress.SpeedBytesPerSecond > 0
                    ? $"{progress.Stage}，下载速度：{DefaultDownloader.FormatSize(progress.SpeedBytesPerSecond, true)}"
                    : progress.Stage);
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private static async Task ObserveInstallAsync(ManagedTask task, TopLevel? topLevel, JavaDistributionVersion version)
    {
        try { await task.Completion; } catch (Exception exception) { Logger.Error(exception); }
        if (topLevel is null) return;
        Dispatcher.UIThread.Post(() => NotificationGateway.Notice(topLevel,
            task.Status == ManagedTaskStatus.Completed ? $"Java {version.MajorVersion} 安装完成" : $"Java {version.MajorVersion} 安装失败",
            task.Status == ManagedTaskStatus.Completed ? NotificationType.Success : NotificationType.Error));
    }
}

public sealed class JavaDistributionItem(JavaDistribution distribution)
{
    public string DisplayName => distribution.DisplayName;
    public string VersionSummary => $"可用 Java {string.Join(", ", distribution.Versions.Select(x => x.MajorVersion).Distinct().OrderByDescending(x => x))}";
    public IReadOnlyList<JavaDistributionVersion> Versions => distribution.Versions;
}

public partial class JavaDownloadPageViewModel : ObservableObject
{
    public ObservableCollection<JavaDistributionItem> Distributions { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial string ErrorText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "正在获取可用发行版…";

    public JavaDownloadPageViewModel() => _ = LoadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        Distributions.Clear();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        try
        {
            foreach (var distribution in await JavaDistributionService.GetDistributionsAsync())
                Distributions.Add(new JavaDistributionItem(distribution));
            StatusText = Distributions.Count > 0 ? $"共 {Distributions.Count} 个发行版" : "没有可用的发行版";
        }
        catch (Exception exception)
        {
            HasError = true;
            ErrorText = $"获取 Java 发行版失败：{exception.Message}";
            StatusText = "获取失败";
            Logger.Error(exception);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
