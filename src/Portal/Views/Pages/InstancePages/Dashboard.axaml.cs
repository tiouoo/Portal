using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Module;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Services;
using Portal.ViewModels;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class Dashboard : Dsc, INotifyPropertyChanged, IDisposable
{
    private readonly InstanceDetailPage _parent;
    private bool _isDisposed;

    public Dashboard(MinecraftInstance instance, InstanceDetailPage parent)
    {
        _parent = parent;
        Instance = instance;
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            InstanceManager.Instance.StatisticsChanged -= OnStatisticsChanged;
            InstanceManager.Instance.InstanceIconChanged -= OnInstanceIconChanged;
            InstanceManager.Instance.StatisticsChanged += OnStatisticsChanged;
            InstanceManager.Instance.InstanceIconChanged += OnInstanceIconChanged;
            RefreshWorldUserIds();
            Instance.StorageUsage.Refresh();
            Dispatcher.UIThread.Post(() => InstanceIcon.Source = Instance[72]);


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

    public MinecraftInstance Instance { get; }
    public ObservableCollection<string> WorldUserIds { get; } = [];

    public string TotalPlayTime
    {
        get
        {
            var seconds = Instance.GetTotalPlayTimeSeconds();
            return seconds < 60
                ? string.Format(CommonLanguageManager.Instance.dashboard_playTimeSeconds.CurrentValue(), seconds)
                : seconds < 3600
                    ? string.Format(CommonLanguageManager.Instance.dashboard_playTimeMinutes.CurrentValue(),
                        seconds / 60.0)
                    : string.Format(CommonLanguageManager.Instance.dashboard_playTimeHours.CurrentValue(),
                        seconds / 3600.0);
        }
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

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => DashboardPropertyChanged += value;
        remove => DashboardPropertyChanged -= value;
    }

    private event PropertyChangedEventHandler? DashboardPropertyChanged;

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
                Text = string.Format(CommonLanguageManager.Instance.dashboard_deleteConfirm.CurrentValue(),
                    Instance.InstanceName),
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.dashboard_deleteTitle.CurrentValue(),
                Mode = DialogMode.Error,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.dashboard_delete.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
                CanLightDismiss = false,
                CanResize = false
            });
        if (result != DialogResult.Yes)
            return;

        if (!InstanceDeletionCoordinator.TryBegin(Instance))
        {
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_deletingInProgress.CurrentValue(),
                NotificationType.Warning);
            return;
        }

        try
        {
            InstanceDeletionCoordinator.CloseRelatedPages(Instance);
            await Task.Run(() =>
            {
                foreach (var path in Instance.GetDeletionPaths())
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                    else if (File.Exists(path))
                        File.Delete(path);
            });
            var folders = Data.ConfigEntry.MinecraftFolders.ToArray();
            var instances = await Task.Run(() => InstanceManager.Instance.ScanAll(folders));
            InstanceManager.Instance.ApplyInstances(instances);
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_instanceDeleted.CurrentValue(),
                NotificationType.Success);
        }
        catch (IOException ex)
        {
            topLevel.Notice(IsFileInUse(ex)
                ? CommonLanguageManager.Instance.dashboard_deleteFileInUse.CurrentValue()
                : string.Format(CommonLanguageManager.Instance.dashboard_deleteFailed.CurrentValue(), ex.Message),
                NotificationType.Error);
        }
        catch (UnauthorizedAccessException)
        {
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_deleteNoPermission.CurrentValue(),
                NotificationType.Error);
        }
        finally
        {
            InstanceDeletionCoordinator.Complete(Instance);
        }
    }

    private static bool IsFileInUse(IOException exception)
    {
        if (exception.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021))
            return true;

        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is Win32Exception { NativeErrorCode: 32 or 33 })
                return true;

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

    private void ToggleChartDays_Click(object? sender, RoutedEventArgs e)
    {
        RecentPlayTimeChart.Days = RecentPlayTimeChart.Days == 7 ? 30 : 7;
        Block.Text = RecentPlayTimeChart.Days != 7
            ? CommonLanguageManager.Instance.dashboard_30days.CurrentValue()
            : CommonLanguageManager.Instance.dashboard_7days.CurrentValue();
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
            : WorldUserIds.FirstOrDefault(userId =>
                  !string.Equals(userId, "Shared", StringComparison.OrdinalIgnoreCase))
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
            Title = CommonLanguageManager.Instance.dashboard_saveIconAs.CurrentValue(),
            SuggestedFileName = "Icon.png",
            FileTypeChoices = [new FilePickerFileType(CommonLanguageManager.Instance.dashboard_pngImage.CurrentValue()) { Patterns = ["*.png"] }]
        });
        if (file == null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            Instance.sourceIcon.Save(stream, PngBitmapEncoderOptions.Default);
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_iconSaved.CurrentValue(), NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.dashboard_saveFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
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
            new IconPickerViewModel(), this.TryGetHostId(), options);
        if (result == null) return;

        try
        {
            await using var stream = result.CustomImageFile != null
                ? await result.CustomImageFile.OpenReadAsync()
                : typeof(MinecraftInstance).Assembly.GetManifestResourceStream(result.BuiltInResourceName!);
            if (stream == null)
                throw new FileNotFoundException(
                    CommonLanguageManager.Instance.dashboard_builtinIconNotFound.CurrentValue());

            using var icon = new Bitmap(stream);
            Instance.SetIcon(icon);
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_iconChanged.CurrentValue(), NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.dashboard_changeFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
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
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_iconReset.CurrentValue(), NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.dashboard_resetFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
        }

        Dispatcher.UIThread.Post(() => InstanceIcon.Source = Instance[72]);
    }

    private void JumpPage(object? sender, PointerPressedEventArgs e)
    {
        var tag = (sender as Control).Tag as string;
        if (tag == "mods")
            _parent.NavigateTo(typeof(Mods));
        else if (tag == "resource")
            _parent.NavigateTo(typeof(ResourcePacks));
        else if (tag == "shader")
            _parent.NavigateTo(typeof(ShaderPacks));
        else if (tag == "saves")
            _parent.NavigateTo(typeof(Saves));
        else if (tag == "bedrock-resource-packs")
            _parent.NavigateTo(typeof(BedrockResourcePacks));
        else if (tag == "bedrock-behavior-packs")
            _parent.NavigateTo(typeof(BedrockBehaviorPacks));
        else if (tag == "bedrock-worlds") _parent.NavigateTo(typeof(BedrockWorlds));
    }

    private async void CreateLink_Click(object? sender, RoutedEventArgs e)
    {
        await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this),
            () => DesktopShortcutService.CreateAsync(Instance));
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
                Title = CommonLanguageManager.Instance.dashboard_modifyVersionTitle.CurrentValue(),
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false
            });
        if (result is null) return;

        var viewModel = (VersionModifyDialogViewModel)dialog.DataContext!;

        _parent.HostTab.Close();
        _ = NotifyModifyOutcomeAsync(viewModel.StartedTask, topLevel);
    }

    private async void RenameInstance_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || !Instance.CanModifyVersion)
            return;

        var newId = await RenameInstanceDialog.Show(Instance, this.TryGetHostId());
        if (string.IsNullOrWhiteSpace(newId))
            return;


        var task = InstanceRenameService.CreateRenameTask(Instance, newId);
        task.Start();
        _parent.HostTab.Close();
        _ = NotifyRenameOutcomeAsync(task, topLevel);
    }

    private async void ExportModpack_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || !Instance.IsJava)
            return;

        var options = await ModpackExportDialogHost.Show(Instance, this.TryGetHostId());
        if (options is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = CommonLanguageManager.Instance.dashboard_exportModpackTitle.CurrentValue(),
            SuggestedFileName = $"{options.PackName} {options.PackVersion}".Trim(),
            FileTypeChoices =
            [
                new FilePickerFileType(CommonLanguageManager.Instance.dashboard_modrinthModpack.CurrentValue())
                {
                    Patterns = ["*.mrpack"]
                }
            ]
        });
        if (file is null)
            return;

        var task = InstanceModpackExportService.CreateExportTask(Instance, options, file.Path.LocalPath);
        task.Start();
        _ = NotifyExportOutcomeAsync(task, topLevel);
    }

    private static async Task NotifyExportOutcomeAsync(ManagedTask task, TopLevel? topLevel)
    {
        if (topLevel is null) return;
        await task.Completion;
        if (task.Status is ManagedTaskStatus.Faulted)
            topLevel.Notice(string.Format(
                CommonLanguageManager.Instance.dashboard_exportModpackFailed.CurrentValue(), task.ErrorMessage),
                NotificationType.Error);
        else if (task.Status is ManagedTaskStatus.Cancelled)
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_exportModpackCancelled.CurrentValue(),
                NotificationType.Warning);
        else
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_exportModpackComplete.CurrentValue(),
                NotificationType.Success);
    }

    private static async Task NotifyRenameOutcomeAsync(ManagedTask task, TopLevel? topLevel)
    {
        if (topLevel is null) return;
        await task.Completion;
        if (task.Status is ManagedTaskStatus.Faulted)
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.dashboard_renameFailed.CurrentValue(),
                task.ErrorMessage), NotificationType.Error);
        else if (task.Status is ManagedTaskStatus.Cancelled)
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_renameCancelled.CurrentValue(),
                NotificationType.Warning);
        else
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_renamed.CurrentValue(), NotificationType.Success);
    }

    private static async Task NotifyModifyOutcomeAsync(ManagedTask? task, TopLevel? topLevel)
    {
        if (task is null || topLevel is null) return;
        await task.Completion;
        if (task.Status is ManagedTaskStatus.Faulted)
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.dashboard_modifyFailed.CurrentValue(),
                task.ErrorMessage), NotificationType.Error);
        else if (task.Status is ManagedTaskStatus.Cancelled)
            topLevel.Notice(CommonLanguageManager.Instance.dashboard_modifyCancelled.CurrentValue(),
                NotificationType.Warning);
    }
}