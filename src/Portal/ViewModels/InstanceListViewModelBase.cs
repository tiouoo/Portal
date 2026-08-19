using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.App.Helpers;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.Extensions;

namespace Portal.ViewModels;

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

public partial class InstanceListViewModelBase : ObservableObject, IDisposable
{
    private readonly ConcurrentDictionary<MinecraftInstance, InstancePinyinCache> _pinyinCache = new();
    private bool _isDisposed;

    private FolderFilterOption? _selectedFolderFilter;

    protected InstanceListViewModelBase()
    {
        InstanceManager.Instance.InstanceIconChanged += OnInstanceIconChanged;
        InstanceManager.Instance.InstancesChanged += OnInstancesChanged;
        BlockListService.Instance.Changed += OnBlockListChanged;
        BlockListService.Instance.UiStateChanged += OnBlockListUiStateChanged;
    }

    public Data Data => Data.Instance;
    public ObservableCollection<MinecraftInstance> FilteredMinecraftInstances { get; set; } = [];

    [ObservableProperty] public partial bool HasFilter { get; set; }
    [ObservableProperty] public partial bool HasFilteredInstances { get; set; }

    public bool HasBlockedInstancesOnly =>
        BlockListService.Instance.HasBlockedInstances(InstanceManager.Instance.Instances);

    public string ToggleBlockedInstancesText =>
        BlockListService.Instance.ShowBlockedInstances
            ? CommonLanguageManager.Instance.instanceList_hideBlocked.CurrentValue()
            : CommonLanguageManager.Instance.instanceList_showBlocked.CurrentValue();

    public bool HasBlockedRecentPlaysOnly =>
        BlockListService.Instance.HasBlockedRecentPlays(GetRecentPlayTargets());

    public string ToggleBlockedRecentPlaysText =>
        BlockListService.Instance.ShowBlockedRecentPlays
            ? CommonLanguageManager.Instance.instanceList_hideBlocked.CurrentValue()
            : CommonLanguageManager.Instance.instanceList_showBlocked.CurrentValue();

    public long TotalPlayTimeSeconds
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(DisplayTotalPlayTime));
                OnPropertyChanged(nameof(PlayTimeUnit));
            }
        }
    }

    public int TotalPlaySessions
    {
        get;
        private set
        {
            if (SetProperty(ref field, value)) OnPropertyChanged(nameof(DisplayTotalPlaySessions));
        }
    }

    public string DisplayTotalPlayTime => FormatPlayTime(TotalPlayTimeSeconds);
    public string DisplayTotalPlaySessions => TotalPlaySessions.ToString();
    public string PlayTimeUnit => GetPlayTimeUnit(TotalPlayTimeSeconds);

    public List<SortOption> SortOptions { get; } =
    [
        new() { DisplayText = CommonLanguageManager.Instance.instanceList_sortName.CurrentValue(), SortType = InstanceSortType.Name },
        new() { DisplayText = CommonLanguageManager.Instance.instanceList_sortRecentPlay.CurrentValue(), SortType = InstanceSortType.PlayTime },
        new() { DisplayText = CommonLanguageManager.Instance.instanceList_sortFolderName.CurrentValue(), SortType = InstanceSortType.FolderName },
        new() { DisplayText = CommonLanguageManager.Instance.instanceList_sortLoader.CurrentValue(), SortType = InstanceSortType.Loader },
        new() { DisplayText = CommonLanguageManager.Instance.instanceList_sortVersion.CurrentValue(), SortType = InstanceSortType.Version }
    ];

    public List<FolderFilterOption> FolderFilterOptions { get; set; } = [];

    public FolderFilterOption? SelectedFolderFilter
    {
        get => _selectedFolderFilter;
        set
        {
            if (SetProperty(ref _selectedFolderFilter, value)) ApplyFilterAndSort();
        }
    }

    public SortOption? SelectedSortOption
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                if (value != null) Data.ConfigEntry.DefaultInstanceSortType = value.SortType;

                ApplyFilterAndSort();
            }
        }
    }

    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value)) ApplyFilterAndSort();
        }
    } = string.Empty;

    public string SummaryText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public MinecraftInstance? RecentInstance
    {
        get;
        private set
        {
            if (SetProperty(ref field, value)) OnPropertyChanged(nameof(HasRecentInstance));
        }
    }

    public bool HasRecentInstance => RecentInstance != null;

    protected virtual bool FolderFilterEnabled => false;

    public virtual void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        InstanceManager.Instance.InstanceIconChanged -= OnInstanceIconChanged;
        InstanceManager.Instance.InstancesChanged -= OnInstancesChanged;
        BlockListService.Instance.Changed -= OnBlockListChanged;
        BlockListService.Instance.UiStateChanged -= OnBlockListUiStateChanged;
        FilteredMinecraftInstances.Clear();
        _pinyinCache.Clear();
        FolderFilterOptions.Clear();
        RecentInstance = null;
    }

    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isDisposed)
            {
                RefreshBlockStates();
                ApplyFilterAndSort();
            }
        });
    }

    private void RefreshBlockStates()
    {
        foreach (var instance in InstanceManager.Instance.Instances)
            instance.IsBlocked = BlockListService.Instance.IsInstanceBlocked(instance);
    }

    private void OnInstanceIconChanged(object? sender, MinecraftInstance instance)
    {
        if (_isDisposed)
            return;

        OnPropertyChanged(nameof(RecentInstance));
        var index = FilteredMinecraftInstances.IndexOf(instance);
        if (index >= 0)
            FilteredMinecraftInstances[index] = instance;
    }

    private void OnBlockListChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isDisposed)
            {
                RefreshBlockStates();
                ApplyFilterAndSort();
                RefreshRecentPlaysForBlockList();
                OnPropertyChanged(nameof(HasBlockedInstancesOnly));
                OnPropertyChanged(nameof(ToggleBlockedInstancesText));
                OnPropertyChanged(nameof(HasBlockedRecentPlaysOnly));
                OnPropertyChanged(nameof(ToggleBlockedRecentPlaysText));
            }
        });
    }

    private void OnBlockListUiStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isDisposed)
            {
                ApplyFilterAndSort();
                RefreshRecentPlaysForBlockList();
                OnPropertyChanged(nameof(HasBlockedInstancesOnly));
                OnPropertyChanged(nameof(ToggleBlockedInstancesText));
                OnPropertyChanged(nameof(HasBlockedRecentPlaysOnly));
                OnPropertyChanged(nameof(ToggleBlockedRecentPlaysText));
            }
        });
    }

    protected virtual void RefreshRecentPlaysForBlockList()
    {
    }

    protected virtual IEnumerable<RecentPlayTarget> GetRecentPlayTargets()
    {
        return [];
    }

    [RelayCommand]
    private void ToggleBlockedInstances()
    {
        BlockListService.Instance.ShowBlockedInstances = !BlockListService.Instance.ShowBlockedInstances;
    }

    [RelayCommand]
    private void ToggleBlockedRecentPlays()
    {
        BlockListService.Instance.ShowBlockedRecentPlays = !BlockListService.Instance.ShowBlockedRecentPlays;
    }

    public void RefreshFolderFilterOptions()
    {
        var currentSelection = _selectedFolderFilter;
        FolderFilterOptions.Clear();
        FolderFilterOptions.Add(new FolderFilterOption { DisplayText = CommonLanguageManager.Instance.instanceList_allFolders.CurrentValue(), FolderName = null });
        foreach (var folder in Data.ConfigEntry.MinecraftFolders)
            FolderFilterOptions.Add(new FolderFilterOption
                { DisplayText = folder.FolderName, FolderName = folder.FolderName });

        _selectedFolderFilter = currentSelection != null
            ? FolderFilterOptions.FirstOrDefault(o => o.FolderName == currentSelection.FolderName)
            : FolderFilterOptions[0];
        OnPropertyChanged(nameof(SelectedFolderFilter));
    }

    public void ApplyFilterAndSort()
    {
        if (_isDisposed)
            return;

        RefreshBlockStates();
        UpdateRecentInstance();
        UpdatePlayStatistics();
        FilteredMinecraftInstances.Clear();
        var query = InstanceManager.Instance.Instances.AsEnumerable();

        if (FolderFilterEnabled)
        {
            var selectedFolder = _selectedFolderFilter?.FolderName;
            if (!string.IsNullOrEmpty(selectedFolder)) query = query.Where(x => x.FolderName == selectedFolder);
        }

        HasFilter = !string.IsNullOrWhiteSpace(SearchText);

        if (!BlockListService.Instance.ShowBlockedInstances)
            query = query.Where(x => !BlockListService.Instance.IsInstanceBlocked(x));

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

        var sortedResult = sortType switch
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
                .ThenBy(x => x.IsVanilla)
        };

        FilteredMinecraftInstances.AddRange(sortedResult);
        UpdateSummaryText(sortedResult);

        HasFilteredInstances = FilteredMinecraftInstances.Count > 0;
        OnPropertyChanged(nameof(HasBlockedInstancesOnly));
        OnPropertyChanged(nameof(ToggleBlockedInstancesText));
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
        var totalSessions = 0;

        foreach (var instance in InstanceManager.Instance.Instances)
        {
            totalTime += instance.GetTotalPlayTimeSeconds();
            totalSessions += instance.Config?.PlaySessions ?? 0;
        }

        TotalPlayTimeSeconds = totalTime;
        TotalPlaySessions = totalSessions;
    }

    public void UpdateStatistics()
    {
        if (_isDisposed)
            return;

        UpdatePlayStatistics();
        UpdateRecentInstance();
    }

    private static string GetPlayTimeUnit(long seconds)
    {
        if (seconds < 60)
            return "s";
        if (seconds < 3600)
            return "min";
        return "h";
    }

    private static string FormatPlayTime(long seconds)
    {
        double value;

        if (seconds < 60)
            value = seconds;
        else if (seconds < 3600)
            value = seconds / 60.0;
        else
            value = seconds / 3600.0;

        return FormatNumber(value);
    }

    private static string FormatNumber(double value)
    {
        if (value < 1000) return value.ToString("F1", CultureInfo.InvariantCulture);

        return ((long)value).ToString();
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
                SummaryText = string.Format(
                    CommonLanguageManager.Instance.instanceList_summaryWithFolders.CurrentValue(),
                    folderCount, totalCount, javaCount, bedrockCount);
            }
            else
            {
                SummaryText = string.Format(
                    CommonLanguageManager.Instance.instanceList_summaryWithoutFolders.CurrentValue(),
                    totalCount, javaCount, bedrockCount);
            }
        }
        else
        {
            var folderCount = list.Select(x => x.FolderName).Distinct().Count();
            SummaryText = string.Format(
                CommonLanguageManager.Instance.instanceList_summaryWithFolders.CurrentValue(),
                folderCount, totalCount, javaCount, bedrockCount);
        }
    }

    protected static Version? ParseVersion(string? versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return null;

        var versionPart = versionId.Split('-')[0];
        if (Version.TryParse(versionPart, out var version)) return version;

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
                LoaderDescriptionFirstLetters =
                    PinyinHelper.GetAllFirstLetters(instance.LoaderDescription ?? string.Empty)
            };
            return cache;
        });
    }
}