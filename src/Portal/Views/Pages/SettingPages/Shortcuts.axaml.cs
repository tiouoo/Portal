using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.App.Helpers;
using Portal.Core.Helpers;
using Portal.Module.AggregatedSearch;
using Portal.Module.Initialize;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("快捷键", "设置/快捷键", "Shortcuts")]
public partial class Shortcuts : DataUserControl
{
    public Shortcuts()
    {
        InitializeComponent();
        DataContext = new ShortcutsViewModel();
        // 页面可能被设置页缓存后复用，重新可见时刷新展示的快捷键
        Loaded += (_, _) => ViewModel.RefreshAllFromConfig();
    }

    public ShortcutsViewModel ViewModel => (ShortcutsViewModel)DataContext!;

    private void ResetOne_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutItemViewModel item }) return;
        Data.ConfigEntry.Shortcuts.ResetAction(item.Action);
        ConfigSaver.SaveConfig();
    }
}

public partial class ShortcutsViewModel : ObservableObject
{
    public ShortcutsViewModel()
    {
        Data.ConfigEntry.Shortcuts.PropertyChanged += OnShortcutsConfigChanged;
        foreach (var category in ShortcutActions.Categories)
            Categories.Add(new ShortcutCategoryViewModel(category));
    }

    public Data Data => Data.Instance;

    /// <summary>按当前搜索条件过滤后的分组。</summary>
    public ObservableCollection<ShortcutCategoryViewModel> Categories { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>快捷键配置变化（录制、恢复默认、清除等）后同步到页面。</summary>
    public void RefreshAllFromConfig()
    {
        foreach (var category in Categories)
        {
            foreach (var item in category.AllItems)
                item.RefreshFromConfig();
        }

        ApplyFilter();
    }

    private void OnShortcutsConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutConfig.Bindings))
            RefreshAllFromConfig();
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        foreach (var category in Categories)
            category.ApplyFilter(query);
    }
}

public partial class ShortcutItemViewModel : ObservableObject
{
    private bool _suppressWriteBack;
    private readonly List<string> _pinyins;
    private readonly List<string> _firstLetters;

    public ShortcutItemViewModel(ShortcutAction action)
    {
        Action = action;
        DisplayName = ShortcutActions.GetDisplayName(action);
        _pinyins = PinyinHelper.GetAllPinyins(DisplayName);
        _firstLetters = PinyinHelper.GetAllFirstLetters(DisplayName);
        RefreshFromConfig();
    }

    public ShortcutAction Action { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial KeyGesture? Gesture { get; set; }
    [ObservableProperty] public partial bool IsNotDefault { get; set; }

    /// <summary>当前没有设置快捷键时显示“（无）”占位。</summary>
    public bool IsEmpty => Gesture is null;

    /// <summary>输入框录制到新组合键时，写回配置。</summary>
    partial void OnGestureChanged(KeyGesture? value)
    {
        if (_suppressWriteBack) return;
        Data.ConfigEntry.Shortcuts.SetGesture(Action,
            value is null ? null : ShortcutActions.GestureToString(value));
        ConfigSaver.SaveConfig();
    }

    /// <summary>按操作名称、当前快捷键或其拼音/首字母模糊匹配搜索。</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var gestureText = Gesture?.ToString("g", null) ?? "未设置";
        var text = $"{DisplayName} {gestureText}";
        if (MatchesToken(query, text)) return true;
        // 空格分隔的多个关键词，命中任意一个即算匹配，例如“打开 下载”
        return query.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
            .Any(token => MatchesToken(token, text));
    }

    private bool MatchesToken(string token, string text) =>
        text.Contains(token, System.StringComparison.OrdinalIgnoreCase) ||
        _pinyins.Any(pinyin => pinyin.Contains(token, System.StringComparison.OrdinalIgnoreCase)) ||
        _firstLetters.Any(pinyin => pinyin.Contains(token, System.StringComparison.OrdinalIgnoreCase));

    public void RefreshFromConfig()
    {
        _suppressWriteBack = true;
        Gesture = ShortcutActions.ParseGesture(ShortcutActions.GetStoredGesture(Action));
        IsNotDefault = (ShortcutActions.GetStoredGesture(Action) ?? string.Empty)
                       != (ShortcutActions.GetDefaultGesture(Action) ?? string.Empty);
        _suppressWriteBack = false;
    }
}

public partial class ShortcutCategoryViewModel : ObservableObject
{
    public ShortcutCategoryViewModel(ShortcutCategory category)
    {
        Name = category.Name;
        AllItems = category.Items.Select(definition => new ShortcutItemViewModel(definition.Action)).ToList();
        Items = new ObservableCollection<ShortcutItemViewModel>(AllItems);
    }

    public string Name { get; }
    public IReadOnlyList<ShortcutItemViewModel> AllItems { get; }
    public ObservableCollection<ShortcutItemViewModel> Items { get; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public void ApplyFilter(string query)
    {
        var matches = query.Length == 0
            ? AllItems
            : AllItems.Where(item => item.Matches(query)).ToList();
        Items.Clear();
        foreach (var item in matches)
            Items.Add(item);
        IsEmpty = Items.Count == 0;
    }
}
