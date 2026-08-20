using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Components.Downloader;
using Portal.Core.Const;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
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
        var selected = await OverlayDialog
            .ShowCustomAsync<JavaVersionDialog, JavaVersionDialogViewModel, JavaDistributionVersion>(
                new JavaVersionDialogViewModel(item), hostId,
                new OverlayDialogOptions
                {
                    Title = string.Format(CommonLanguageManager.Instance.javaDownload_selectVersion.CurrentValue(),
                        item.DisplayName),
                    Buttons = DialogButton.None,
                    CanLightDismiss = false, CanResize = false
                });
        if (selected is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        var expectedFolder = Path.Combine(ConfigPath.JavaRuntimesPath, $"{selected.Vendor}-{selected.MajorVersion}");
        var duplicate = Data.ConfigEntry.JavaRuntimes.Any(x => x.MajorVersion == selected.MajorVersion &&
                                                               x.JavaPath.StartsWith(expectedFolder,
                                                                   StringComparison.OrdinalIgnoreCase));
        if (duplicate && topLevel is not null)
        {
            var result = await OverlayDialog.ShowStandardAsync(new TextBlock
            {
                Margin = new Thickness(24),
                Text = string.Format(CommonLanguageManager.Instance.javaDownload_duplicateText.CurrentValue(),
                    selected.Vendor, selected.MajorVersion),
                TextWrapping = TextWrapping.Wrap
            }, null, hostId, new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.javaDownload_alreadyInstalledTitle.CurrentValue(),
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.javaDownload_reinstall.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
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
            Name = string.Format(CommonLanguageManager.Instance.javaDownload_installTaskName.CurrentValue(),
                version.Vendor, version.MajorVersion),
            Description = CommonLanguageManager.Instance.javaDownload_preparingDownload.CurrentValue(),
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.minecraft_cancelInstall.CurrentValue(),
                    Description = CommonLanguageManager.Instance.javaDownload_cancelInstallDescription.CurrentValue(),
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
            context.SetRunning(CommonLanguageManager.Instance.javaDownload_downloadingJava.CurrentValue());
            var runtime = await JavaDistributionService.InstallAsync(version, ConfigPath.JavaRuntimesPath,
                ConfigPath.TempFolderPath, progress => ReportInstallProgress(context, progress),
                context.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Data.ConfigEntry.JavaRuntimes.Contains(runtime)) Data.ConfigEntry.JavaRuntimes.Add(runtime);
                if (Data.ConfigEntry.GetJavaDefault(runtime.MajorVersion) is null)
                    Data.ConfigEntry.SetJavaDefault(runtime.MajorVersion, runtime);
            });
            context.ReportProgress(1);
            context.SetDescription(string.Format(
                CommonLanguageManager.Instance.javaDownload_installed.CurrentValue(), version.MajorVersion));
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
                    ? $"{progress.Stage}{string.Format(CommonLanguageManager.Instance.minecraft_javaInstallSpeed.CurrentValue(), DefaultDownloader.FormatSize(progress.SpeedBytesPerSecond, true))}"
                    : progress.Stage);
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private static async Task ObserveInstallAsync(ManagedTask task, TopLevel? topLevel, JavaDistributionVersion version)
    {
        try
        {
            await task.Completion;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }

        if (topLevel is null) return;
        Dispatcher.UIThread.Post(() => topLevel.Notice(
            task.Status == ManagedTaskStatus.Completed
                ? string.Format(CommonLanguageManager.Instance.javaDownload_installComplete.CurrentValue(),
                    version.MajorVersion)
                : string.Format(CommonLanguageManager.Instance.javaDownload_installFailed.CurrentValue(),
                    version.MajorVersion),
            task.Status == ManagedTaskStatus.Completed ? NotificationType.Success : NotificationType.Error));
    }
}

public sealed class JavaDistributionItem(JavaDistribution distribution)
{
    public string DisplayName => distribution.DisplayName;

    public string VersionSummary =>
        string.Format(CommonLanguageManager.Instance.javaDownload_availableJava.CurrentValue(),
            string.Join(", ",
                distribution.Versions.Select(x => x.MajorVersion).Distinct().OrderByDescending(x => x)));

    public IReadOnlyList<JavaDistributionVersion> Versions => distribution.Versions;
}

public partial class JavaDownloadPageViewModel : ObservableObject
{
    public JavaDownloadPageViewModel()
    {
        _ = LoadAsync();
    }

    public ObservableCollection<JavaDistributionItem> Distributions { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial string ErrorText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } =
        CommonLanguageManager.Instance.javaDownload_fetchingDistributions.CurrentValue();

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
            StatusText = Distributions.Count > 0
                ? string.Format(CommonLanguageManager.Instance.javaDownload_distributionCount.CurrentValue(),
                    Distributions.Count)
                : CommonLanguageManager.Instance.javaDownload_noDistributions.CurrentValue();
        }
        catch (Exception exception)
        {
            HasError = true;
            ErrorText = string.Format(CommonLanguageManager.Instance.javaDownload_fetchDistributionsFailed.CurrentValue(),
                exception.Message);
            StatusText = CommonLanguageManager.Instance.javaDownload_fetchFailed.CurrentValue();
            Logger.Error(exception);
        }
        finally
        {
            IsLoading = false;
        }
    }
}