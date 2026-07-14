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
using CommunityToolkit.Mvvm.Input;
using MinecraftLaunch.Base.Enums;
using Portal.Classes.Entries;
using Portal.Classes.Enums;
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
    public NewTabViewModel NewTabViewModel;

    public NewTabPage()
    {
        InitializeComponent();
        NewTabViewModel = new NewTabViewModel();
        DataContext = NewTabViewModel;
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

    private void FavoritedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var instance = (sender as Control)?.Tag as MinecraftInstance;
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        NewTabViewModel.ApplyFilterAndSort();
    }

    private void ButtonOpenInstance_OnClick(object? sender, RoutedEventArgs e)
    {
        var tab = new TabEntry(sender.AsTopLevel() as TioTabWindowBase, new InstancesPage());
        var tioTabWindowBase = sender.AsTopLevel() as TioTabWindowBase;
        tioTabWindowBase?.CreateTab(tab);
        tioTabWindowBase?.SelectTab(tab);
    }
}

public class SortOption
{
    public string DisplayText { get; set; }
    public InstanceSortType SortType { get; set; }
}

public partial class NewTabViewModel : ObservableObject
{
    public Data Data => Data.Instance;
    public ObservableCollection<MinecraftInstance> FilteredMinecraftInstances { get; set; } = [];

    public List<SortOption> SortOptions { get; } = new()
    {
        new SortOption { DisplayText = "名称", SortType = InstanceSortType.Name },
        new SortOption { DisplayText = "游玩时间", SortType = InstanceSortType.PlayTime },
        new SortOption { DisplayText = "文件夹名称", SortType = InstanceSortType.FolderName },
        new SortOption { DisplayText = "加载器", SortType = InstanceSortType.Loader },
        new SortOption { DisplayText = "版本", SortType = InstanceSortType.Version },
    };

    private SortOption? _selectedSortOption;

    public SortOption? SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(ref _selectedSortOption, value))
            {
                if (value != null)
                {
                    Data.ConfigEntry.DefaultInstanceSortType = value.SortType;
                }

                ApplyFilterAndSort();
            }
        }
    }

    public NewTabViewModel()
    {
        _selectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
        ApplyFilterAndSort();
    }

    [RelayCommand]
    public void ToggleFavorite(MinecraftInstance instance)
    {
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
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
                (!string.IsNullOrEmpty(x.InstanceName) &&
                 x.InstanceName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (x.Config?.Note != null && x.Config.Note.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.VersionId) &&
                 x.VersionId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            );
        }

        var cultureInfo = CultureInfo.GetCultureInfo("zh-CN");
        var stringComparer = StringComparer.Create(cultureInfo, true);

        var sortType = SelectedSortOption?.SortType ?? InstanceSortType.Name;

        // 三级排序结构：
        // 第一级：收藏在前，未收藏在后
        // 第二级：在收藏/未收藏各自组内，按选择的排序方式排列
        // 第三级：第二级相同时，有 mod 加载器的在前，原版在后
        IOrderedEnumerable<MinecraftInstance> sortedResult = sortType switch
        {
            InstanceSortType.Name => query
                .OrderByDescending(x => x.Config?.IsFavorite ?? false)
                .ThenBy(x => x.InstanceName ?? string.Empty, stringComparer)
                .ThenBy(x => x.IsVanilla),

            InstanceSortType.PlayTime => query
                .OrderByDescending(x => x.Config?.IsFavorite ?? false)
                .ThenByDescending(x => x.LastPlayTime == DateTime.MinValue ? 0 : 1)
                .ThenByDescending(x => x.LastPlayTime)
                .ThenBy(x => x.IsVanilla),

            InstanceSortType.FolderName => query
                .OrderByDescending(x => x.Config?.IsFavorite ?? false)
                .ThenBy(x => x.FolderName ?? string.Empty, stringComparer)
                .ThenBy(x => x.IsVanilla),

            InstanceSortType.Loader => query
                .OrderByDescending(x => x.Config?.IsFavorite ?? false)
                .ThenByDescending(x => x.LoaderDescription, stringComparer)
                .ThenBy(x => x.IsVanilla),

            InstanceSortType.Version => query
                .OrderByDescending(x => x.Config?.IsFavorite ?? false)
                .ThenByDescending(x => ParseVersion(x.VersionId))
                .ThenBy(x => x.IsVanilla),

            _ => query
                .OrderByDescending(x => x.Config?.IsFavorite ?? false)
                .ThenBy(x => x.InstanceName ?? string.Empty, stringComparer)
                .ThenBy(x => x.IsVanilla),
        };

        FilteredMinecraftInstances.AddRange(sortedResult);
    }

    private Version? ParseVersion(string? versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return null;

        var versionPart = versionId.Split('-')[0];
        if (Version.TryParse(versionPart, out var version))
        {
            return version;
        }

        if (versionPart.StartsWith("1."))
        {
            var parts = versionPart.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            {
                var patch = parts.Length >= 3 && int.TryParse(parts[2], out var p) ? p : 0;
                return new Version(1, minor, patch);
            }
        }

        return null;
    }
}