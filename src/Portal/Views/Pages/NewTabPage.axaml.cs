using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages;

public partial class NewTabPage : DataUserControl, ITioTabPage
{
    public NewTabPage()
    {
        InitializeComponent();
        DataContext = new NewTabViewModel();
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "新标签页",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M96.5,160L96.5,309.5C96.5,326.5,103.2,342.8,115.2,354.8L307.2,546.8C332.2,571.8,372.7,571.8,397.7,546.8L547.2,397.3C572.2,372.3,572.2,331.8,547.2,306.8L355.2,114.8C343.2,102.7,327,96,310,96L160.5,96C125.2,96,96.5,124.7,96.5,160z M208.5,176C226.2,176 240.5,190.3 240.5,208 240.5,225.7 226.2,240 208.5,240 190.8,240 176.5,225.7 176.5,208 176.5,190.3 190.8,176 208.5,176z")
    };

    public TabEntry HostTab { get; set; }

    private void InputElement_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ScrollViewer.Offset = new Vector(
            ScrollViewer.Offset.X + e.Delta.Y * -232,
            ScrollViewer.Offset.Y
        );
        e.Handled = true;
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        if (UiProperty.MinecraftInstances.Count == 0)
        {
            sender.AsTopLevel().Notice("还没有实例可以抽签哦", NotificationType.Error);
            return;
        }

        OverlayDialogOptions options = new()
        {
            Title = "开奖啦！",
            IsCloseButtonVisible = false,
            Buttons = DialogButton.YesNoCancel,
            OverrideNoButtonText = "再来亿次",
            OverrideYesButtonText = "就它了",
            OverrideCancelButtonText = "不玩了",
            CanLightDismiss = false,
            CanDragMove = true,
            CanResize = true,
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110,
        };
        _ = Show();

        return;

        async Task Show()
        {
            var result = UiProperty.MinecraftInstances.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
            var feed = await OverlayDialog.ShowCustomAsync<RandomMinecraft, RandomMinecraftViewModle, string>(
                new RandomMinecraftViewModle(result), sender.AsTopLevel().TryGetHostId(), options: options);

            if (feed == "again")
            {
                _ = Show();
                return;
            }
            
            // TODO: Handle the result
        }
    }
}

public partial class NewTabViewModel : ObservableObject
{
    public Data Data => Data.Instance;
    public ObservableCollection<MinecraftInstance> FilteredMinecraftInstances { get; set; } = [];

    public NewTabViewModel()
    {
        ApplyFilterAndSort();
    }

    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ApplyFilterAndSort();
            }
        }
    } = string.Empty;

    public void ApplyFilterAndSort()
    {
        FilteredMinecraftInstances.Clear();
        var query = UiProperty.MinecraftInstances.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            query = query.Where(x =>
                (x.FolderName != null && x.FolderName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (x.MinecraftEntry?.Id != null &&
                 x.MinecraftEntry.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (x.Config?.Note != null && x.Config.Note.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (x.MinecraftEntry?.Version.VersionId != null &&
                 x.MinecraftEntry.Version.VersionId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            );
        }

        var cultureInfo = CultureInfo.GetCultureInfo("zh-CN");
        var stringComparer = StringComparer.Create(cultureInfo, true);

        var sortedResult = query
            .OrderByDescending(x => x.Config?.IsFavorite ?? false)
            .ThenBy(x => x.MinecraftEntry?.IsVanilla ?? true)
            .ThenBy(x => x.MinecraftEntry?.Id ?? string.Empty, stringComparer);

        FilteredMinecraftInstances.AddRange(sortedResult);
    }
}