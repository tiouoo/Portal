using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Classes.Config;
using Portal.Core.App.Helpers;
using Portal.Core.Classes.Config;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Module.Initialize;
using Portal.Localization;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("pages_shortcuts", "pages_shortcutsPath", "Shortcuts")]
public partial class Shortcuts : Dsc
{
    public Shortcuts()
    {
        InitializeComponent();
        DataContext = new ShortcutsViewModel();

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

    public ObservableCollection<ShortcutCategoryViewModel> Categories { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    public void RefreshAllFromConfig()
    {
        foreach (var category in Categories)
        foreach (var item in category.AllItems)
            item.RefreshFromConfig();

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
    private readonly List<string> _firstLetters;
    private readonly List<string> _pinyins;
    private bool _suppressWriteBack;

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

    public bool IsEmpty => Gesture is null;

    partial void OnGestureChanged(KeyGesture? value)
    {
        if (_suppressWriteBack) return;
        Data.ConfigEntry.Shortcuts.SetGesture(Action,
            value is null ? null : ShortcutActions.GestureToString(value));
        ConfigSaver.SaveConfig();
    }

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var gestureText = Gesture?.ToString("g", null) ??
                          CommonLanguageManager.Instance.shortcuts_notSet.CurrentValue();
        var text = $"{DisplayName} {gestureText}";
        if (MatchesToken(query, text)) return true;

        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => MatchesToken(token, text));
    }

    private bool MatchesToken(string token, string text)
    {
        return text.Contains(token, StringComparison.OrdinalIgnoreCase) ||
               _pinyins.Any(pinyin => pinyin.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
               _firstLetters.Any(pinyin => pinyin.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

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