using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.Widgets;
using Portal.Core.Services;
using Portal.Views.Pages;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Widgets;

public partial class QuickWorldWidget1x1 : InstanceBoundWidgetBase
{
    private bool _loading;
    private WorldSaveInfo? _world;

    public QuickWorldWidget1x1()
    {
        Size = new WidgetCellSize(1, 1);
        InitializeComponent();
    }

    protected override void OnInstanceResolved()
    {
        RefreshInstanceDisplay();
        _ = LoadWorldAsync();
    }

    protected override void OnInstanceIconRefreshed()
    {
        RefreshInstanceDisplay();
    }

    private void RefreshInstanceDisplay()
    {
        var iconImage = this.FindControl<Image>("IconImage");
        var sourceText = this.FindControl<TextBlock>("SourceText");
        var launchButton = this.FindControl<Button>("LaunchButton");

        var instance = Instance;
        if (launchButton != null)
            launchButton.IsVisible =
                instance is { MinecraftEntry: { } entry } && entry.ReleaseTime > new DateTime(2023, 4, 4);

        if (instance == null)
        {
            if (iconImage != null) iconImage.Source = null;
            if (sourceText != null) sourceText.Text = string.Empty;
            return;
        }

        if (iconImage != null) iconImage.Source = instance[56];
        if (sourceText != null) sourceText.Text = instance.InstanceName;
    }

    private async Task LoadWorldAsync()
    {
        var instance = Instance;
        var folderName = GetData<QuickWorldWidgetData>()?.WorldFolderName;
        if (instance == null || string.IsNullOrEmpty(folderName))
        {
            _world = null;
            RefreshWorldDisplay();
            return;
        }

        _loading = true;
        try
        {
            var service = new WorldSaveService();
            _world = await service.ReadAsync(instance, folderName);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Widget] Failed to load world {folderName} for {instance.InstanceName}: {exception}");
            _world = null;
        }
        finally
        {
            _loading = false;
        }

        RefreshWorldDisplay();
    }

    private void RefreshWorldDisplay()
    {
        var titleText = this.FindControl<TextBlock>("TitleText");

        var world = _world;
        var folderName = GetData<QuickWorldWidgetData>()?.WorldFolderName;

        if (world == null)
        {
            if (titleText != null)
                titleText.Text = _loading ? "加载中…" : folderName ?? "未选择存档";
            return;
        }

        if (titleText != null)
            titleText.Text = string.IsNullOrWhiteSpace(world.LevelName) ? world.FolderName : world.LevelName;
    }

    public override async void PerformClick()
    {
        var instance = Instance;
        var folderName = GetData<QuickWorldWidgetData>()?.WorldFolderName;
        if (instance == null || string.IsNullOrEmpty(folderName))
            return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var world = _world;
        if (world == null)
        {
            var service = new WorldSaveService();
            world = await service.ReadAsync(instance, folderName);
            if (world == null)
                return;
            _world = world;
            RefreshWorldDisplay();
        }

        await OverlayDialog.ShowCustomAsync<WorldSaveDetails, WorldSaveDetailsViewModel, object>(
            new WorldSaveDetailsViewModel(world, instance), this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Mode = DialogMode.None,
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                IsCloseButtonVisible = true,
                CloseBtnMargin = new Thickness(0, 12, 12, 0)
            });
    }

    private void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Instance == null || GetData<QuickWorldWidgetData>()?.WorldFolderName is not { } folderName)
            return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var world = _world;
        var target = new RecentPlayTarget(
            Instance,
            RecentPlayTargetType.World,
            folderName,
            world != null && !string.IsNullOrWhiteSpace(world.LevelName) ? world.LevelName : folderName,
            world != null ? $"存档·{world.Version ?? "未知版本"}·{GetGameModeText(world.GameMode)}" : "存档",
            world?.LastPlayedTime ?? DateTime.MinValue,
            world?.IconPath);

        _ = MinecraftLaunchService.LaunchAsync(Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(Instance, logSession => MinecraftLogPage.Open(logSession, topLevel)),
            target);
    }

    private static string GetGameModeText(int? gameMode)
    {
        return gameMode switch { 0 => "生存", 1 => "创造", 2 => "冒险", 3 => "旁观", _ => "未知模式" };
    }
}