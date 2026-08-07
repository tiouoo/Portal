using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MinecraftLaunch.Base.Models.Game;
using Portal.Const;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Module.DesktopShortcut;
using Portal.Services;
using Portal.ViewModels;
using Portal.Views.Pages.DownloadPages;
using Portal.Views.SubWindows;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class Dashboard : DataUserControl, INotifyPropertyChanged, IDisposable
{
    private InstanceDetailPage _parent;
    private event PropertyChangedEventHandler? DashboardPropertyChanged;
    private bool _isDisposed;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => DashboardPropertyChanged += value;
        remove => DashboardPropertyChanged -= value;
    }

    public MinecraftInstance Instance { get; }
    public ObservableCollection<string> WorldUserIds { get; } = [];

    public string TotalPlayTime
    {
        get
        {
            var seconds = Instance.GetTotalPlayTimeSeconds();
            return seconds < 60 ? $"{seconds} 秒" :
                seconds < 3600 ? $"{seconds / 60.0:F1} 分钟" : $"{seconds / 3600.0:F1} 小时";
        }
    }

    public Dashboard(MinecraftInstance instance, InstanceDetailPage parent)
    {
        _parent = parent;
        Instance = instance;
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            // 页面被 InstanceDetailPage 缓存复用，Unloaded 时会退订事件，
            // 因此在 Loaded 中重新订阅（先退订以避免重复订阅）
            InstanceManager.Instance.StatisticsChanged -= OnStatisticsChanged;
            InstanceManager.Instance.InstanceIconChanged -= OnInstanceIconChanged;
            InstanceManager.Instance.StatisticsChanged += OnStatisticsChanged;
            InstanceManager.Instance.InstanceIconChanged += OnInstanceIconChanged;
            RefreshWorldUserIds();
            Instance.StorageUsage.Refresh();
            Dispatcher.UIThread.Post(() => InstanceIcon.Source = Instance[72]);

            // 如果嵌入在 OverlayWindow 中，隐藏编辑和启动按钮
            if (TopLevel.GetTopLevel(this) is OverlayWindow)
            {
                EditButton.IsVisible = false;
                LaunchButton.IsVisible = false;
            }
        };
        Unloaded += (_, _) =>
        {
            InstanceManager.Instance.StatisticsChanged -= OnStatisticsChanged;
            InstanceManager.Instance.InstanceIconChanged -= OnInstanceIconChanged;
        };
    }

    public Dashboard()
    {
        InitializeComponent();
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            _ = (sender as Control)!.GetTopLevel().Launcher
                .LaunchDirectoryInfoAsync(new DirectoryInfo(Instance.InstanceFolderPath));
    }

    private void LaunchInstance_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        _ = MinecraftLaunchService.LaunchAsync(Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(Instance, logSession =>
            {
                if (topLevel != null)
                    MinecraftLogPage.Open(logSession, topLevel);
            }));
    }

    private void OpenInstanceFolder_Click(object? sender, RoutedEventArgs e)
    {
        _ = TopLevel.GetTopLevel(this)?.Launcher
            .LaunchDirectoryInfoAsync(new DirectoryInfo(Instance.InstanceFolderPath));
    }

    private async void DeleteInstance_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || !Directory.Exists(Instance.InstanceFolderPath))
            return;

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = $"确定要永久删除实例“{Instance.InstanceName}”吗？此操作无法撤销。",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "删除实例",
                Mode = DialogMode.Error,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "删除",
                OverrideNoButtonText = "取消",
                CanLightDismiss = false,
                CanResize = false
            });
        if (result != DialogResult.Yes)
            return;

        if (!InstanceDeletionCoordinator.TryBegin(Instance))
        {
            NotificationGateway.Notice(topLevel, "该实例正在删除中。", NotificationType.Warning);
            return;
        }

        try
        {
            InstanceDeletionCoordinator.CloseRelatedPages(Instance);
            await Task.Run(() =>
            {
                foreach (var path in Instance.GetDeletionPaths())
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                    else if (File.Exists(path))
                        File.Delete(path);
                }
            });
            var folders = Data.ConfigEntry.MinecraftFolders.ToArray();
            var instances = await Task.Run(() => InstanceManager.Instance.ScanAll(folders));
            InstanceManager.Instance.ApplyInstances(instances);
            NotificationGateway.Notice(topLevel, "实例已删除", NotificationType.Success);
        }
        catch (IOException ex)
        {
            NotificationGateway.Notice(topLevel, IsFileInUse(ex)
                ? "无法删除实例：实例文件正在被其他程序占用。请先关闭正在运行的游戏、文件管理器或占用该实例文件夹的程序，再重试删除。"
                : $"无法删除实例：{ex.Message}", NotificationType.Error);
        }
        catch (UnauthorizedAccessException)
        {
            NotificationGateway.Notice(topLevel, "无法删除实例：没有删除此实例的权限。", NotificationType.Error);
        }
        finally
        {
            InstanceDeletionCoordinator.Complete(Instance);
        }
    }

    /// <summary>
    /// 判断 IOException 是否为文件/目录被其他进程占用（共享冲突或锁定冲突）。
    /// </summary>
    private static bool IsFileInUse(IOException exception)
    {
        if (exception.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021))
            return true;

        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is Win32Exception { NativeErrorCode: 32 or 33 })
                return true;
        }

        return false;
    }

    private void OnStatisticsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed) return;
            DashboardPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPlayTime)));
            RecentPlayTimeChart.InvalidateVisual();
        });
    }

    private void OnInstanceIconChanged(object? sender, MinecraftInstance instance)
    {
        if (!ReferenceEquals(instance, Instance)) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isDisposed)
                InstanceIcon.Source = Instance[72];
        });
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        InstanceManager.Instance.StatisticsChanged -= OnStatisticsChanged;
        InstanceManager.Instance.InstanceIconChanged -= OnInstanceIconChanged;
        DataContext = null;
    }

    private void ToggleChartDays_Click(object? sender, RoutedEventArgs e)
    {
        RecentPlayTimeChart.Days = RecentPlayTimeChart.Days == 7 ? 30 : 7;
        Block.Text = RecentPlayTimeChart.Days != 7 ? "30 天" : "7 天";
    }

    private void RefreshWorldUserIds()
    {
        if (Instance.BedrockConfig is not { } config) return;

        var selectedUserId = WorldUserIdSelector.SelectedItem as string;
        var userIds = BedrockDataPathResolver.GetWorldUserIds(config);
        WorldUserIds.Clear();
        foreach (var userId in userIds) WorldUserIds.Add(userId);
        WorldUserIdSelector.SelectedItem = selectedUserId != null && WorldUserIds.Contains(selectedUserId)
            ? selectedUserId
            : WorldUserIds.FirstOrDefault(userId => !string.Equals(userId, "Shared", StringComparison.OrdinalIgnoreCase))
              ?? WorldUserIds.FirstOrDefault();
    }

    private async void WorldUserIdSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        await Instance.StorageUsage.RefreshBedrockWorldsAsync();
    }

    private void SaveIcon_Click(object? sender, RoutedEventArgs e)
    {
        _ = SaveIconAsync();
    }

    private async Task SaveIconAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "将图标另存为",
            SuggestedFileName = "Icon.png",
            FileTypeChoices = [new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }]
        });
        if (file == null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            Instance.sourceIcon.Save(stream, PngBitmapEncoderOptions.Default);
            NotificationGateway.Notice(topLevel, "图标已保存", NotificationType.Success);
        }
        catch (Exception ex)
        {
            NotificationGateway.Notice(topLevel, $"保存失败：{ex.Message}", NotificationType.Error);
        }

        Dispatcher.UIThread.Post(() => InstanceIcon.Source = Instance[72]);
    }

    private void ChangeIcon_Click(object? sender, RoutedEventArgs e)
    {
        _ = ChangeIconAsync();
    }

    private async Task ChangeIconAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
        };
        var result = await OverlayDialog.ShowCustomAsync<IconPicker, IconPickerViewModel, IconPickerResult>(
            new IconPickerViewModel(), hostId: this.TryGetHostId(), options: options);
        if (result == null) return;

        try
        {
            await using var stream = result.CustomImageFile != null
                ? await result.CustomImageFile.OpenReadAsync()
                : typeof(MinecraftInstance).Assembly.GetManifestResourceStream(result.BuiltInResourceName!);
            if (stream == null)
                throw new FileNotFoundException("未找到所选的内置图标。");

            using var icon = new Avalonia.Media.Imaging.Bitmap(stream);
            Instance.SetIcon(icon);
            NotificationGateway.Notice(topLevel, "图标已更换", NotificationType.Success);
        }
        catch (Exception ex)
        {
            NotificationGateway.Notice(topLevel, $"更换失败：{ex.Message}", NotificationType.Error);
        }

        Dispatcher.UIThread.Post(() => InstanceIcon.Source = Instance[72]);
    }

    private void ResetIcon_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            Instance.ResetIcon();
            NotificationGateway.Notice(topLevel, "图标已重置", NotificationType.Success);
        }
        catch (Exception ex)
        {
            NotificationGateway.Notice(topLevel, $"重置失败：{ex.Message}", NotificationType.Error);
        }

        Dispatcher.UIThread.Post(() => InstanceIcon.Source = Instance[72]);
    }

    private void JumpPage(object? sender, PointerPressedEventArgs e)
    {
        var tag = (sender as Control).Tag as string;
        if (tag == "mods")
        {
            _parent.NavigateTo(typeof(Mods));
        }
        else if (tag == "resource")
        {
            _parent.NavigateTo(typeof(ResourcePacks));
        }
        else if (tag == "shader")
        {
            _parent.NavigateTo(typeof(ShaderPacks));
        }
        else if (tag == "saves")
        {
            _parent.NavigateTo(typeof(Saves));
        }
        else if (tag == "bedrock-resource-packs")
        {
            _parent.NavigateTo(typeof(BedrockResourcePacks));
        }
        else if (tag == "bedrock-behavior-packs")
        {
            _parent.NavigateTo(typeof(BedrockBehaviorPacks));
        }
        else if (tag == "bedrock-worlds")
        {
            _parent.NavigateTo(typeof(BedrockWorlds));
        }
    }

    private async void CreateLink_Click(object? sender, RoutedEventArgs e)
    {
        await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this), () => DesktopShortcutService.CreateAsync(Instance));
    }

    private async void ModifyVersion_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || !Instance.CanModifyVersion)
            return;

        var dialog = new VersionModifyDialog(Instance);
        var result = await OverlayDialog.ShowCustomAsync<VersionModifyDialogResult>(dialog, dialog.DataContext,
            this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "修改版本",
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false
            });
        if (result is null) return;

        var viewModel = (VersionModifyDialogViewModel)dialog.DataContext!;
        // 修改任务在后台继续执行，直接关闭当前实例详情页；任务状态可在任务面板查看
        _parent.HostTab.Close();
        _ = NotifyModifyOutcomeAsync(viewModel.StartedTask, topLevel);
    }

    private static async Task NotifyModifyOutcomeAsync(ManagedTask? task, TopLevel? topLevel)
    {
        if (task is null || topLevel is null) return;
        await task.Completion;
        if (task.Status is ManagedTaskStatus.Faulted)
            NotificationGateway.Notice(topLevel, $"版本修改失败：{task.ErrorMessage}", NotificationType.Error);
        else if (task.Status is ManagedTaskStatus.Cancelled)
            NotificationGateway.Notice(topLevel, "版本修改已取消，实例未发生改动。", NotificationType.Warning);
    }
}
