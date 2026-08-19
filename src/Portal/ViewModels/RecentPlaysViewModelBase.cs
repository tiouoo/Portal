using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Views.Pages;

namespace Portal.ViewModels;

public abstract partial class RecentPlaysViewModelBase : InstanceListViewModelBase
{
    private readonly RecentPlayListService _recentPlayListService = RecentPlayListService.Instance;
    private List<RecentPlayItem> _allRecentPlays = [];
    private List<RecentPlayItem>? _visibleRecentPlays;
    private bool _isDisposed;
    private int _recentPlayCapacity = 1;

    protected RecentPlaysViewModelBase()
    {
        _recentPlayListService.Refreshed += OnRecentPlaysRefreshed;
        UpdateRecentPlays(_recentPlayListService.Items);
    }

    public ObservableCollection<RecentPlayItem> RecentPlays { get; } = [];

    public bool HasRecentPlays => _allRecentPlays.Count > 0;

    public bool CanExpandRecentPlays => GetVisibleRecentPlays().Count > _recentPlayCapacity;

    public string ToggleRecentPlaysText =>
        RecentPlaysExpanded
            ? CommonLanguageManager.Instance.recentPlay_collapse.CurrentValue()
            : string.Format(CommonLanguageManager.Instance.recentPlay_expandAll.CurrentValue(),
                GetVisibleRecentPlays().Count);

    protected abstract bool RecentPlaysExpanded { get; set; }

    protected List<RecentPlayItem> GetVisibleRecentPlays()
    {
        return _visibleRecentPlays ??= BlockListService.Instance.ShowBlockedRecentPlays
            ? _allRecentPlays
            : _allRecentPlays.Where(item => !BlockListService.Instance.IsRecentPlayBlocked(item.Target)).ToList();
    }

    protected override void RefreshRecentPlaysForBlockList()
    {
        foreach (var item in _allRecentPlays)
            item.RefreshBlockState();
        _visibleRecentPlays = null;
        ApplyRecentPlayCapacity();
    }

    protected override IEnumerable<RecentPlayTarget> GetRecentPlayTargets()
    {
        return _allRecentPlays.Select(item => item.Target);
    }

    protected virtual void UpdateRecentPlays(IEnumerable<RecentPlayTarget> targets)
    {
        RecentPlays.Clear();
        DisposeRecentPlays();
        _allRecentPlays = [.. targets.Select(target => new RecentPlayItem(target))];
        _visibleRecentPlays = null;
        SortRecentPlays();
        ApplyRecentPlayCapacity();
    }

    private void OnRecentPlaysRefreshed(object? sender, EventArgs e)
    {
        UpdateRecentPlays(_recentPlayListService.Items);
    }

    public void SetRecentPlayWidth(double width)
    {
        var capacity = Math.Max(1, (int)(width / 282));
        if (_recentPlayCapacity == capacity)
            return;

        _recentPlayCapacity = capacity;
        ApplyRecentPlayCapacity();
    }

    private void ApplyRecentPlayCapacity()
    {
        var visiblePlays = GetVisibleRecentPlays();
        var take = RecentPlaysExpanded ? visiblePlays.Count : _recentPlayCapacity;
        RecentPlays.Clear();
        foreach (var item in visiblePlays.Take(take))
            RecentPlays.Add(item);
        OnPropertyChanged(nameof(HasRecentPlays));
        OnPropertyChanged(nameof(CanExpandRecentPlays));
        OnPropertyChanged(nameof(ToggleRecentPlaysText));
        OnPropertyChanged(nameof(HasBlockedRecentPlaysOnly));
        OnPropertyChanged(nameof(ToggleBlockedRecentPlaysText));
    }

    [RelayCommand]
    private void ToggleRecentPlays()
    {
        RecentPlaysExpanded = !RecentPlaysExpanded;
        ApplyRecentPlayCapacity();
    }

    public void ToggleRecentPlayFavorite(RecentPlayItem item)
    {
        item.ToggleFavorite();
        SortRecentPlays();
        _visibleRecentPlays = null;
        ApplyRecentPlayCapacity();
    }

    private void SortRecentPlays()
    {
        _allRecentPlays =
        [
            .. _allRecentPlays
                .OrderByDescending(item => item.IsFavorite)
                .ThenByDescending(item => item.LastPlayedTime)
        ];
    }

    public override void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _recentPlayListService.Refreshed -= OnRecentPlaysRefreshed;
        DisposeRecentPlays();
        RecentPlays.Clear();
        base.Dispose();
    }

    protected virtual void DisposeRecentPlays()
    {
        _visibleRecentPlays = null;
        foreach (var item in _allRecentPlays)
            item.Dispose();
        _allRecentPlays.Clear();
    }
}
