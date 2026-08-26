using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Module;
using Portal.Views.Pages;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Widgets;

public abstract class InstanceBoundWidgetBase : IWidgetContent, IWidgetContextMenuProvider
{
    protected MinecraftInstance? Instance;
    protected WidgetLayoutData? LayoutData;

    protected virtual WidgetClickAction DefaultClickAction => WidgetClickAction.None;
    protected virtual IReadOnlyList<(WidgetClickAction Action, string Header)> ClickActionOptions => [];
    protected virtual bool CanPlayFromContextMenu => Instance != null;

    protected WidgetClickAction ClickAction =>
        GetData<InstanceBoundWidgetData>()?.ClickAction ?? DefaultClickAction;

    protected bool CanQuickEnterWorld =>
        Instance?.MinecraftEntry is { } entry && entry.ReleaseTime > new DateTime(2023, 4, 4);

    public override void Initialize(WidgetLayoutData layout)
    {
        LayoutData = layout;
        ResolveInstance();
        OnInstanceResolved();

        InstanceManager.Instance.InstancesChanged += OnInstancesChanged;
        InstanceManager.Instance.InstanceIconChanged += OnInstanceIconChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.InstancesChanged -= OnInstancesChanged;
        InstanceManager.Instance.InstanceIconChanged -= OnInstanceIconChanged;
        Unloaded -= OnUnloaded;
    }

    private void ResolveInstance()
    {
        var path = (LayoutData?.Data as InstanceBoundWidgetData)?.InstanceFolderPath;
        Instance = path != null
            ? InstanceManager.Instance.Instances.FirstOrDefault(i => i.InstanceFolderPath == path)
            : null;
    }

    protected T? GetData<T>() where T : class
    {
        return LayoutData?.Data as T;
    }

    public IReadOnlyList<MenuItem> CreateContextMenuItems(Action saveLayout)
    {
        var items = new List<MenuItem>();

        if (CanPlayFromContextMenu)
        {
            var playItem = new MenuItem
            {
                Header = WidgetsLanguageManager.Instance.contextmenu_play.CurrentValue(),
                Icon = IconResources.CreateIcon("\ue613", 18)
            };
            playItem.Click += (_, _) => PlayFromContextMenu();
            items.Add(playItem);
        }

        var detailsItem = new MenuItem
        {
            Header = WidgetsLanguageManager.Instance.contextmenu_viewDetails.CurrentValue(),
            Icon = IconResources.CreateIcon("\ue60c", 18)
        };
        detailsItem.Click += (_, _) => ViewDetailsFromContextMenu();
        items.Add(detailsItem);

        if (ClickActionOptions.Count == 0)
            return items;

        var clickActionMenu = new MenuItem
        {
            Header = WidgetsLanguageManager.Instance.contextmenu_clickAction.CurrentValue(),
            Icon = IconResources.CreateIcon("\ue607", 18)
        };

        foreach (var option in ClickActionOptions)
        {
            var item = new MenuItem
            {
                Header = option.Header,
                IsChecked = ClickAction == option.Action,
                Classes = { "hide-icon" }
            };
            item.Click += (_, _) =>
            {
                if (GetData<InstanceBoundWidgetData>() is not { } data)
                    return;
                data.ClickAction = option.Action;
                saveLayout();
            };
            clickActionMenu.Items.Add(item);
        }

        items.Add(clickActionMenu);
        return items;
    }

    protected virtual void ViewDetailsFromContextMenu()
    {
        OpenInstanceDetails();
    }

    protected virtual void PlayFromContextMenu()
    {
        LaunchInstance();
    }

    protected void OpenInstanceDetails()
    {
        if (Instance != null && TopLevel.GetTopLevel(this) is { } topLevel)
            InstanceDetailPage.Open(Instance, topLevel);
    }

    protected void LaunchInstance()
    {
        if (Instance == null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _ = MinecraftLaunchService.LaunchAsync(Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(Instance, logSession => MinecraftLogPage.Open(logSession, topLevel)));
    }

    protected async Task PickAndQuickEnterWorldAsync()
    {
        var instance = Instance;
        if (instance == null || !CanQuickEnterWorld || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var result = await OverlayDialog
            .ShowCustomAsync<WorldPickerDialog, WorldPickerDialogViewModel, object?>(
                new WorldPickerDialogViewModel(instance), this.TryGetHostId(), new OverlayDialogOptions
                {
                    Buttons = DialogButton.None,
                    CanLightDismiss = true,
                    CanDragMove = true,
                    CanResize = true,
                    IsCloseButtonVisible = true
                });
        if (result is not WorldPickItem world)
            return;

        var info = world.Info;
        var target = new RecentPlayTarget(
            instance,
            RecentPlayTargetType.World,
            info.FolderName,
            string.IsNullOrWhiteSpace(info.LevelName) ? info.FolderName : info.LevelName,
            string.Format(CommonLanguageManager.Instance.recentPlay_saveDescription.CurrentValue(),
                info.Version ?? CommonLanguageManager.Instance.recentPlay_unknownVersion.CurrentValue(),
                GetGameModeText(info.GameMode)),
            info.LastPlayedTime ?? DateTime.MinValue,
            info.IconPath);

        _ = MinecraftLaunchService.LaunchAsync(instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(instance, logSession => MinecraftLogPage.Open(logSession, topLevel)),
            target);
    }

    protected static string GetGameModeText(int? gameMode)
    {
        return gameMode switch
        {
            0 => CommonLanguageManager.Instance.recentPlay_gameModeSurvival.CurrentValue(),
            1 => CommonLanguageManager.Instance.recentPlay_gameModeCreative.CurrentValue(),
            2 => CommonLanguageManager.Instance.recentPlay_gameModeAdventure.CurrentValue(),
            3 => CommonLanguageManager.Instance.recentPlay_gameModeSpectator.CurrentValue(),
            _ => CommonLanguageManager.Instance.recentPlay_gameModeUnknown.CurrentValue()
        };
    }

    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        var previous = Instance;
        ResolveInstance();
        if (previous != Instance)
            Dispatcher.UIThread.Post(OnInstanceResolved);
    }

    private void OnInstanceIconChanged(object? sender, MinecraftInstance instance)
    {
        if (Instance != null && instance.InstanceFolderPath == Instance.InstanceFolderPath)
            Dispatcher.UIThread.Post(OnInstanceIconRefreshed);
    }

    protected virtual void OnInstanceResolved()
    {
    }

    protected virtual void OnInstanceIconRefreshed()
    {
        OnInstanceResolved();
    }
}
