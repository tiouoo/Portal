using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Tio.Avalonia.Standard.Modules.Extensions;

namespace Portal.Views.Pages;

public class SortOption
{
    public string DisplayText { get; set; } = string.Empty;
    public InstanceSortType SortType { get; set; }
}

public class FolderFilterOption
{
    public string DisplayText { get; set; } = string.Empty;
    public string? FolderName { get; set; }
}

public partial class InstanceListViewModelBase : ObservableObject
{
    public Data Data => Data.Instance;
    public ObservableCollection<MinecraftInstance> FilteredMinecraftInstances { get; set; } = [];

    public List<SortOption> SortOptions { get; } =
    [
        new SortOption { DisplayText = "名称", SortType = InstanceSortType.Name },
        new SortOption { DisplayText = "游玩时间", SortType = InstanceSortType.PlayTime },
        new SortOption { DisplayText = "文件夹名称", SortType = InstanceSortType.FolderName },
        new SortOption { DisplayText = "加载器", SortType = InstanceSortType.Loader },
        new SortOption { DisplayText = "版本", SortType = InstanceSortType.Version },
    ];

    public List<FolderFilterOption> FolderFilterOptions { get; set; } = [];

    private FolderFilterOption? _selectedFolderFilter;
    public FolderFilterOption? SelectedFolderFilter
    {
        get => _selectedFolderFilter;
        set
        {
            if (SetProperty(ref _selectedFolderFilter, value))
            {
                ApplyFilterAndSort();
            }
        }
    }

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

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilterAndSort();
            }
        }
    }

    protected virtual bool FolderFilterEnabled => false;

    public void RefreshFolderFilterOptions()
    {
        var currentSelection = _selectedFolderFilter;
        FolderFilterOptions.Clear();
        FolderFilterOptions.Add(new FolderFilterOption { DisplayText = "所有文件夹", FolderName = null });
        foreach (var folder in Data.ConfigEntry.MinecraftFolders)
        {
            FolderFilterOptions.Add(new FolderFilterOption { DisplayText = folder.FolderName, FolderName = folder.FolderName });
        }
        _selectedFolderFilter = currentSelection != null
            ? FolderFilterOptions.FirstOrDefault(o => o.FolderName == currentSelection.FolderName)
            : FolderFilterOptions[0];
        OnPropertyChanged(nameof(SelectedFolderFilter));
    }

    public void ApplyFilterAndSort()
    {
        FilteredMinecraftInstances.Clear();
        var query = InstanceManager.Instance.Instances.AsEnumerable();

        if (FolderFilterEnabled)
        {
            var selectedFolder = _selectedFolderFilter?.FolderName;
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                query = query.Where(x => x.FolderName == selectedFolder);
            }
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            query = query.Where(x =>
                (x.FolderName != null && x.FolderName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.InstanceName) &&
                 x.InstanceName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (x.Config?.Note != null && x.Config.Note.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.VersionId) &&
                 x.VersionId.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.VersionType) &&
                 x.VersionType.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.Description) &&
                 x.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.LoaderDescription) &&
                 x.LoaderDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase))
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

    protected static Version? ParseVersion(string? versionId)
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
