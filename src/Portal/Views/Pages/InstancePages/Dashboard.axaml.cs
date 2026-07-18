using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MinecraftLaunch.Base.Models.Game;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Services;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class Dashboard : DataUserControl, INotifyPropertyChanged
{
    private InstanceDetailPage _parent;
    private event PropertyChangedEventHandler? DashboardPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => DashboardPropertyChanged += value;
        remove => DashboardPropertyChanged -= value;
    }

    public MinecraftInstance Instance { get; }

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
        InstanceManager.Instance.StatisticsChanged += OnStatisticsChanged;
        InstanceManager.Instance.InstanceIconChanged += OnInstanceIconChanged;
        Loaded += (_, _) =>
        {
            Instance.StorageUsage.Refresh();
            Dispatcher.UIThread.Post(() => InstanceIcon.Source = Instance[72]);
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
            MinecraftLaunchOptionsFactory.Create(logSession =>
            {
                if (topLevel != null)
                    MinecraftLogPage.Open(logSession, topLevel);
            }));
    }

    private void OnStatisticsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DashboardPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPlayTime)));
            RecentPlayTimeChart.InvalidateVisual();
        });
    }

    private void OnInstanceIconChanged(object? sender, MinecraftInstance instance)
    {
        if (!ReferenceEquals(instance, Instance)) return;

        Dispatcher.UIThread.Post(() => InstanceIcon.Source = Instance[72]);
    }

    private void ToggleChartDays_Click(object? sender, RoutedEventArgs e)
    {
        RecentPlayTimeChart.Days = RecentPlayTimeChart.Days == 7 ? 30 : 7;
        Block.Text = RecentPlayTimeChart.Days != 7 ? "30 天" : "7 天";
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
            _parent.NavigateTo(null);
        }
        else if (tag == "resource")
        {
            _parent.NavigateTo(null);
        }
        else if (tag == "shader")
        {
            _parent.NavigateTo(null);
        }
        else if (tag == "saves")
        {
            _parent.NavigateTo(typeof(Saves));
        }
        else if (tag == "bedrock-resource-packs")
        {
            _parent.NavigateTo(typeof(ResourcePacks));
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
}
