using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

public partial class InstancesPage : DataUserControl, ITioTabPage
{
    public InstancesPageViewModel InstancesPageViewModel;

    public InstancesPage()
    {
        InitializeComponent();
        InstancesPageViewModel = new InstancesPageViewModel();
        DataContext = InstancesPageViewModel;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "实例",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M480,576L192,576C139,576,96,533,96,480L96,160C96,107,139,64,192,64L496,64C522.5,64,544,85.5,544,112L544,400C544,420.9,530.6,438.7,512,445.3L512,512C529.7,512 544,526.3 544,544 544,561.7 529.7,576 512,576L480,576z M192,448C174.3,448 160,462.3 160,480 160,497.7 174.3,512 192,512L448,512 448,448 192,448z M224,216C224,229.3,234.7,240,248,240L424,240C437.3,240 448,229.3 448,216 448,202.7 437.3,192 424,192L248,192C234.7,192,224,202.7,224,216z M248,288C234.7,288 224,298.7 224,312 224,325.3 234.7,336 248,336L424,336C437.3,336 448,325.3 448,312 448,298.7 437.3,288 424,288L248,288z")
    };

    public TabEntry HostTab { get; set; }

    private void FavoritedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var instance = (sender as Control)?.Tag as MinecraftInstance;
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        InstancesPageViewModel.ApplyFilterAndSort();
    }
}

public partial class InstancesPageViewModel : ObservableObject
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

    public InstancesPageViewModel()
    {
        _selectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
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