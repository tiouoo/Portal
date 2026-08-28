using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Portal.Localization;

namespace Portal.Views.Pages.InstancePages;

public enum ResourceSortMode
{
    FileName = 0,

    Name = 1,

    LastWriteTime = 2,

    FileSize = 3
}

public enum ResourceFilterMode
{
    All = 0,

    Enabled = 1,

    Disabled = 2,

    Duplicates = 3,

    CanUpdate = 4
}

public sealed class ResourceFilterOption(string label) : INotifyPropertyChanged
{
    private string _label = label;

    public string Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString()
    {
        return _label;
    }
}

public sealed class FilterSortMenuController
{
    private readonly DropDownButton _button;
    private readonly MenuItem[] _filterItems;
    private readonly string[] _filterBaseNames;
    private readonly Action<int> _onFilterChanged;
    private readonly Action<int> _onSortChanged;
    private readonly MenuItem[] _sortItems;
    private int _filterIndex;
    private int _sortIndex;

    public FilterSortMenuController(DropDownButton button, string sortHeader, string filterHeader,
        IReadOnlyList<ResourceFilterOption> filterOptions, string[] filterBaseNames,
        Action<int> onSortChanged, Action<int> onFilterChanged)
    {
        _button = button;
        _filterBaseNames = filterBaseNames;
        _onSortChanged = onSortChanged;
        _onFilterChanged = onFilterChanged;

        var flyout = new MenuFlyout();

        var sortMenu = new MenuItem { Header = sortHeader, Classes = { "hide-icon" } };
        _sortItems = new MenuItem[ResourceListUi.SortOptions.Length];
        for (var i = 0; i < ResourceListUi.SortOptions.Length; i++)
        {
            var index = i;
            var item = new MenuItem { Header = ResourceListUi.SortOptions[index], Classes = { "hide-icon" } };
            item.Click += (_, _) => SelectSort(index);
            _sortItems[i] = item;
            sortMenu.Items.Add(item);
        }

        var filterMenu = new MenuItem { Header = filterHeader, Classes = { "hide-icon" } };
        _filterItems = new MenuItem[filterOptions.Count];
        for (var i = 0; i < filterOptions.Count; i++)
        {
            var index = i;
            var item = new MenuItem { Header = filterOptions[index].Label, Classes = { "hide-icon" } };
            item.Click += (_, _) => SelectFilter(index);
            _filterItems[i] = item;
            filterMenu.Items.Add(item);
        }

        flyout.Items.Add(sortMenu);
        flyout.Items.Add(filterMenu);
        _button.Flyout = flyout;

        UpdateChecks();
        _button.Content = FilterSortText;
    }

    public string FilterSortText => $"{_filterBaseNames[_filterIndex]} | {ResourceListUi.SortOptions[_sortIndex]}";

    public void SetFilterIndex(int index)
    {
        _filterIndex = Math.Clamp(index, 0, _filterItems.Length - 1);
        UpdateChecks();
        _button.Content = FilterSortText;
    }

    public void SetSortIndex(int index)
    {
        _sortIndex = Math.Clamp(index, 0, _sortItems.Length - 1);
        UpdateChecks();
        _button.Content = FilterSortText;
    }

    public void SyncFilterLabels(IReadOnlyList<ResourceFilterOption> filterOptions)
    {
        for (var i = 0; i < _filterItems.Length && i < filterOptions.Count; i++)
            _filterItems[i].Header = filterOptions[i].Label;
    }

    private void SelectFilter(int index)
    {
        SetFilterIndex(index);
        _onFilterChanged(index);
    }

    private void SelectSort(int index)
    {
        SetSortIndex(index);
        _onSortChanged(index);
    }

    private void UpdateChecks()
    {
        for (var i = 0; i < _sortItems.Length; i++)
            _sortItems[i].IsChecked = i == _sortIndex;
        for (var i = 0; i < _filterItems.Length; i++)
            _filterItems[i].IsChecked = i == _filterIndex;
    }
}

public static class ResourceListUi
{
    public static string[] SortOptions { get; } =
    [
        CommonLanguageManager.Instance.resourceList_sortFileName.CurrentValue(),
        CommonLanguageManager.Instance.resourceList_sortResourceName.CurrentValue(),
        CommonLanguageManager.Instance.resourceList_sortAddedTime.CurrentValue(),
        CommonLanguageManager.Instance.resourceList_sortFileSize.CurrentValue()
    ];

    public static string FormatSize(long size)
    {
        if (size < 0)
            size = 0;
        if (size < 1024)
            return $"{size} B";

        var value = (double)size;
        foreach (var unit in new[] { "KiB", "MiB", "GiB", "TiB" })
        {
            value /= 1024.0;
            if (value < 1024.0 || unit == "TiB")
                return $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}";
        }

        return $"{size} B";
    }

    public static long GetFolderSize(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                return 0;
            return Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static string BuildFilterLabel(string name, int count)
    {
        return $"{name}({count})";
    }
}
