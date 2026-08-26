using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.Widgets;
using Portal.Localization;
using Portal.Views.Pages;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Widgets;

public partial class QuickWorldWidget2x1 : InstanceBoundWidgetBase
{
    private bool _loading;
    private WorldSaveInfo? _world;

    protected override WidgetClickAction DefaultClickAction => WidgetClickAction.ShowDetails;
    protected override bool CanPlayFromContextMenu => CanQuickEnterWorld;

    protected override IReadOnlyList<(WidgetClickAction Action, string Header)> ClickActionOptions =>
        CanQuickEnterWorld
            ?
            [
                (WidgetClickAction.QuickEnterWorld,
                    WidgetsLanguageManager.Instance.contextmenu_quickEnterWorld.CurrentValue()),
                (WidgetClickAction.ShowDetails,
                    WidgetsLanguageManager.Instance.contextmenu_showWorldDetails.CurrentValue()),
            ]
            :
            [
                (WidgetClickAction.ShowDetails,
                    WidgetsLanguageManager.Instance.contextmenu_showWorldDetails.CurrentValue())
            ];

    protected override void ViewDetailsFromContextMenu()
    {
        _ = ShowWorldDetailsAsync();
    }

    protected override void PlayFromContextMenu()
    {
        QuickEnterWorld();
    }

    public QuickWorldWidget2x1()
    {
        Size = new WidgetCellSize(2, 1);
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
        var instanceText = this.FindControl<TextBlock>("InstanceText");
        var launchButton = this.FindControl<Button>("LaunchButton");

        var instance = Instance;
        if (launchButton != null)
            launchButton.IsVisible =
                instance is { MinecraftEntry: { } entry } && entry.ReleaseTime > new DateTime(2023, 4, 4);

        if (instance == null)
        {
            if (iconImage != null) iconImage.Source = null;
            if (instanceText != null) instanceText.Text = string.Empty;
            return;
        }

        if (iconImage != null) iconImage.Source = instance[40];
        if (instanceText != null) instanceText.Text = instance.ShortDisplay;
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
        var folderText = this.FindControl<TextBlock>("FolderText");
        var lastPlayText = this.FindControl<TextBlock>("LastPlayText");

        var world = _world;
        var folderName = GetData<QuickWorldWidgetData>()?.WorldFolderName;

        if (world == null)
        {
            if (titleText != null)
                titleText.Text = _loading
                    ? CommonLanguageManager.Instance.widgets_loading.CurrentValue()
                    : folderName ?? CommonLanguageManager.Instance.widgets_noSave.CurrentValue();
            if (folderText != null) folderText.Text = folderName ?? string.Empty;
            if (lastPlayText != null) lastPlayText.Text = string.Empty;
            return;
        }

        if (titleText != null)
            titleText.Text = string.IsNullOrWhiteSpace(world.LevelName) ? world.FolderName : world.LevelName;
        if (folderText != null) folderText.Text = world.FolderName;
        if (lastPlayText != null)
        {
            var time = world.LastPlayedTime ?? world.LastWriteTime;
            lastPlayText.Text = time == DateTime.MinValue
                ? CommonLanguageManager.Instance.widgets_notPlayed.CurrentValue()
                : string.Format(CommonLanguageManager.Instance.saves_lastPlayed.CurrentValue(), time);
        }
    }

    public override async void PerformClick()
    {
        if (ClickAction == WidgetClickAction.QuickEnterWorld && CanQuickEnterWorld)
        {
            QuickEnterWorld();
            return;
        }

        await ShowWorldDetailsAsync();
    }

    private async Task ShowWorldDetailsAsync()
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
        QuickEnterWorld();
    }

    private void QuickEnterWorld()
    {
        if (!CanQuickEnterWorld || Instance == null ||
            GetData<QuickWorldWidgetData>()?.WorldFolderName is not { } folderName)
            return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var world = _world;
        var target = new RecentPlayTarget(
            Instance,
            RecentPlayTargetType.World,
            folderName,
            world != null && !string.IsNullOrWhiteSpace(world.LevelName) ? world.LevelName : folderName,
            world != null
                ? string.Format(CommonLanguageManager.Instance.recentPlay_saveDescription.CurrentValue(),
                    world.Version ?? CommonLanguageManager.Instance.recentPlay_unknownVersion.CurrentValue(),
                    GetGameModeText(world.GameMode))
                : CommonLanguageManager.Instance.favorite_kindSave.CurrentValue(),
            world?.LastPlayedTime ?? DateTime.MinValue,
            world?.IconPath);

        _ = MinecraftLaunchService.LaunchAsync(Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(Instance, logSession => MinecraftLogPage.Open(logSession, topLevel)),
            target);
    }
}