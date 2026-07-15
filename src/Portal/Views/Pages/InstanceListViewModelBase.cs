using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Core.Helpers;
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

internal class InstancePinyinCache
{
    public List<string> InstanceNamePinyins { get; set; } = [];
    public List<string> InstanceNameFirstLetters { get; set; } = [];
    public List<string> FolderNamePinyins { get; set; } = [];
    public List<string> FolderNameFirstLetters { get; set; } = [];
    public List<string> NotePinyins { get; set; } = [];
    public List<string> NoteFirstLetters { get; set; } = [];
    public List<string> DescriptionPinyins { get; set; } = [];
    public List<string> DescriptionFirstLetters { get; set; } = [];
    public List<string> LoaderDescriptionPinyins { get; set; } = [];
    public List<string> LoaderDescriptionFirstLetters { get; set; } = [];
}

public partial class InstanceListViewModelBase : ObservableObject
{
    public Data Data => Data.Instance;
    public ObservableCollection<MinecraftInstance> FilteredMinecraftInstances { get; set; } = [];

    private readonly ConcurrentDictionary<MinecraftInstance, InstancePinyinCache> _pinyinCache = new();

    private long _totalPlayTimeSeconds;
    public long TotalPlayTimeSeconds
    {
        get => _totalPlayTimeSeconds;
        private set
        {
            if (SetProperty(ref _totalPlayTimeSeconds, value))
            {
                OnPropertyChanged(nameof(DisplayTotalPlayTime));
                OnPropertyChanged(nameof(PlayTimeUnit));
            }
        }
    }

    private int _totalPlaySessions;
    public int TotalPlaySessions
    {
        get => _totalPlaySessions;
        private set
        {
            if (SetProperty(ref _totalPlaySessions, value))
            {
                OnPropertyChanged(nameof(DisplayTotalPlaySessions));
            }
        }
    }

    public string DisplayTotalPlayTime => FormatPlayTime(TotalPlayTimeSeconds);
    public string DisplayTotalPlaySessions => TotalPlaySessions.ToString();
    public string PlayTimeUnit => GetPlayTimeUnit(TotalPlayTimeSeconds);

    public List<SortOption> SortOptions { get; } =
    [
        new() { DisplayText = "名称", SortType = InstanceSortType.Name },
        new() { DisplayText = "最近游玩", SortType = InstanceSortType.PlayTime },
        new() { DisplayText = "文件夹名称", SortType = InstanceSortType.FolderName },
        new() { DisplayText = "加载器", SortType = InstanceSortType.Loader },
        new() { DisplayText = "版本", SortType = InstanceSortType.Version },
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

    public string SummaryText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    private MinecraftInstance? _recentInstance;
    public MinecraftInstance? RecentInstance
    {
        get => _recentInstance;
        private set
        {
            if (SetProperty(ref _recentInstance, value))
            {
                OnPropertyChanged(nameof(HasRecentInstance));
            }
        }
    }

    public bool HasRecentInstance => RecentInstance != null;

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
        UpdateRecentInstance();
        UpdatePlayStatistics();
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
            var keyword = SearchText.Trim().ToLowerInvariant();
            query = query.Where(x =>
            {
                var cache = GetOrCreatePinyinCache(x);
                return
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
                     x.LoaderDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                    cache.FolderNamePinyins.Any(p => p.Contains(keyword)) ||
                    cache.FolderNameFirstLetters.Any(p => p.Contains(keyword)) ||
                    cache.InstanceNamePinyins.Any(p => p.Contains(keyword)) ||
                    cache.InstanceNameFirstLetters.Any(p => p.Contains(keyword)) ||
                    cache.NotePinyins.Any(p => p.Contains(keyword)) ||
                    cache.NoteFirstLetters.Any(p => p.Contains(keyword)) ||
                    cache.DescriptionPinyins.Any(p => p.Contains(keyword)) ||
                    cache.DescriptionFirstLetters.Any(p => p.Contains(keyword)) ||
                    cache.LoaderDescriptionPinyins.Any(p => p.Contains(keyword)) ||
                    cache.LoaderDescriptionFirstLetters.Any(p => p.Contains(keyword));
            });
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
        UpdateSummaryText(sortedResult);
    }

    private void UpdateRecentInstance()
    {
        var recent = InstanceManager.Instance.Instances
            .Where(x => x.LastPlayTime != DateTime.MinValue)
            .OrderByDescending(x => x.LastPlayTime)
            .FirstOrDefault();
        RecentInstance = recent;
    }

    private void UpdatePlayStatistics()
    {
        long totalTime = 0;
        int totalSessions = 0;

        foreach (var instance in InstanceManager.Instance.Instances)
        {
            totalTime += instance.GetTotalPlayTimeSeconds();
            totalSessions += instance.Config?.PlaySessions ?? 0;
        }

        TotalPlayTimeSeconds = totalTime;
        TotalPlaySessions = totalSessions;
    }

    /// <summary>
    /// 公开方法，用于更新统计数据（支持外部调用）
    /// </summary>
    public void UpdateStatistics()
    {
        UpdatePlayStatistics();
    }

    /// <summary>
    /// 获取游玩时长单位
    /// </summary>
    private static string GetPlayTimeUnit(long seconds)
    {
        if (seconds < 60)
            return "s";
        if (seconds < 3600)
            return "min";
        return "h";
    }

    /// <summary>
    /// 格式化游玩时长，自动判断单位（秒/分钟/小时）
    /// 小于1000保留一位小数，大于等于1000只保留整数
    /// </summary>
    private static string FormatPlayTime(long seconds)
    {
        double value;
        
        if (seconds < 60)
        {
            value = seconds;
        }
        else if (seconds < 3600)
        {
            value = seconds / 60.0;
        }
        else
        {
            value = seconds / 3600.0;
        }
        
        return FormatNumber(value);
    }

    /// <summary>
    /// 格式化数字：小于1000保留一位小数，大于等于1000只保留整数
    /// </summary>
    private static string FormatNumber(double value)
    {
        if (value < 1000)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture);
        }
        else
        {
            return ((long)value).ToString();
        }
    }

    private void UpdateSummaryText(IEnumerable<MinecraftInstance> instances)
    {
        var list = instances.ToList();
        var totalCount = list.Count;
        var javaCount = list.Count(x => x.Type == MinecraftInstanceType.Java);
        var bedrockCount = list.Count(x => x.Type == MinecraftInstanceType.Bedrock);

        if (FolderFilterEnabled)
        {
            var selectedFolder = _selectedFolderFilter?.FolderName;
            if (string.IsNullOrEmpty(selectedFolder))
            {
                var folderCount = list.Select(x => x.FolderName).Distinct().Count();
                SummaryText = $"找到{folderCount}个文件夹的{totalCount}个实例\nJava版{javaCount}个，基岩版{bedrockCount}个";
            }
            else
            {
                SummaryText = $"找到{totalCount}个实例\nJava版{javaCount}个，基岩版{bedrockCount}个";
            }
        }
        else
        {
            var folderCount = list.Select(x => x.FolderName).Distinct().Count();
            SummaryText = $"找到{folderCount}个文件夹的{totalCount}个实例\nJava版{javaCount}个，基岩版{bedrockCount}个";
        }
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

    private InstancePinyinCache GetOrCreatePinyinCache(MinecraftInstance instance)
    {
        return _pinyinCache.GetOrAdd(instance, _ =>
        {
            var cache = new InstancePinyinCache
            {
                InstanceNamePinyins = PinyinHelper.GetAllPinyins(instance.InstanceName ?? string.Empty),
                InstanceNameFirstLetters = PinyinHelper.GetAllFirstLetters(instance.InstanceName ?? string.Empty),
                FolderNamePinyins = PinyinHelper.GetAllPinyins(instance.FolderName ?? string.Empty),
                FolderNameFirstLetters = PinyinHelper.GetAllFirstLetters(instance.FolderName ?? string.Empty),
                NotePinyins = PinyinHelper.GetAllPinyins(instance.Config?.Note ?? string.Empty),
                NoteFirstLetters = PinyinHelper.GetAllFirstLetters(instance.Config?.Note ?? string.Empty),
                DescriptionPinyins = PinyinHelper.GetAllPinyins(instance.Description ?? string.Empty),
                DescriptionFirstLetters = PinyinHelper.GetAllFirstLetters(instance.Description ?? string.Empty),
                LoaderDescriptionPinyins = PinyinHelper.GetAllPinyins(instance.LoaderDescription ?? string.Empty),
                LoaderDescriptionFirstLetters = PinyinHelper.GetAllFirstLetters(instance.LoaderDescription ?? string.Empty),
            };
            return cache;
        });
    }
}
