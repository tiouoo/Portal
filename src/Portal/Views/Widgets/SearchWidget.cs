using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Portal.Core.Classes.Entries;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Module.Widgets;
using Portal.Localization;
using Portal.Module;
using Portal.Views.Pages;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using AutoCompleteBox = TioUi.Controls.AutoCompleteBox;

namespace Portal.Views.Widgets;

public sealed class SearchWidget : IWidgetContent
{
    private static readonly string SearchIconData = "SearchGeometry";

    private readonly Panel _innerLeftPlaceholder;
    private readonly ComboBox _modeComboBox;
    private readonly Grid _root;
    private readonly AutoCompleteBox _searchBox;
    private readonly IReadOnlyList<SearchMode> _searchModes = [];
    private SearchMode? _selectedMode;

    public SearchWidget(WidgetCellSize size)
    {
        Size = size;

        _searchModes = StartPageViewModel.DefaultSearchModes;
        _selectedMode = _searchModes.FirstOrDefault();


        _modeComboBox = new ComboBox
        {
            Theme = (ControlTheme)Application.Current!.FindResource("BareComboBox")!,
            ItemsSource = _searchModes,
            MaxDropDownHeight = 999,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 0, 0)
        };
        _modeComboBox.ItemTemplate = new FuncDataTemplate<SearchMode>((mode, _) =>
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new PathIcon
                    {
                        Data = mode?.IconData is { Length: > 0 } key ? GeometryResources.Get(key) : null,
                        Height = 16,
                        Width = 16,
                        IsVisible = !string.IsNullOrEmpty(mode?.IconData)
                    },
                    new TextBlock
                        { Text = mode?.DisplayText ?? string.Empty, VerticalAlignment = VerticalAlignment.Center }
                }
            });
        _modeComboBox.SelectionChanged += (_, _) =>
        {
            if (_modeComboBox.SelectedItem is SearchMode m)
                _selectedMode = m;
        };
        _modeComboBox.SelectedIndex = 0;


        _searchBox = new AutoCompleteBox
        {
            FilterMode = AutoCompleteFilterMode.None,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = CommonLanguageManager.Instance.common_search.CurrentValue(),
            MaxDropDownHeight = 336
        };


        _innerLeftPlaceholder = new Panel { Width = 0 };
        _modeComboBox.SizeChanged += (_, e) =>
            _innerLeftPlaceholder.Width = e.NewSize.Width;
        _searchBox.InnerLeftContent = _innerLeftPlaceholder;

        var searchIcon = new PathIcon
        {
            Data = GeometryResources.Get(SearchIconData),
            Height = 14,
            Width = 14
        };
        searchIcon.Bind(
            ForegroundProperty,
            searchIcon.GetResourceObservable("InnerForegroundColor")
        );
        _searchBox.InnerRightContent = searchIcon;


        _searchBox.ItemTemplate = new FuncDataTemplate<AggregatedSearchEntry>((entry, _) =>
        {
            var typeText = new TextBlock
            {
                FontSize = 12,
                Text = entry?.TypeDescription ?? string.Empty
            };
            typeText.Bind(TextBlock.ForegroundProperty, _searchBox.GetResourceObservable("InnerForegroundColor"));

            var descText = new TextBlock
            {
                FontSize = 12,
                MaxLines = 1,
                Text = entry?.Description ?? string.Empty,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            descText.Bind(TextBlock.ForegroundProperty, _searchBox.GetResourceObservable("InnerForegroundColor"));

            return new StackPanel
            {
                Margin = new Thickness(4, 0),
                Children =
                {
                    new TextBlock
                    {
                        MaxLines = 1,
                        Text = entry?.Title ?? string.Empty,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap
                    },
                    new StackPanel
                    {
                        Margin = new Thickness(0, 2, 0, 0),
                        Orientation = Orientation.Horizontal,
                        Spacing = 5,
                        Children = { typeText, descText }
                    }
                }
            };
        });

        _searchBox.Populating += (_, e) =>
        {
            if (_selectedMode?.PageType is not null)
            {
                e.Cancel = true;
                return;
            }

            _searchBox.ItemsSource = Searcher.Search(e.Parameter ?? string.Empty);
        };
        _searchBox.DropDownOpened += (_, _) =>
        {
            if (_selectedMode?.PageType is not null)
            {
                _searchBox.IsDropDownOpen = false;
                return;
            }

            _searchBox.ItemsSource = Searcher.Search(_searchBox.Text ?? string.Empty);
        };
        _searchBox.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count == 0 || e.AddedItems[0] is not AggregatedSearchEntry entry)
                return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            _searchBox.SelectedItem = null;
            _searchBox.Text = null;
            _searchBox.IsDropDownOpen = false;
            AggregatedSearchHandler.HandleAsync(entry, topLevel);
        };
        _searchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Bubble, true);


        _root = new Grid();
        _root.Children.Add(_searchBox);
        _root.Children.Add(_modeComboBox);
        Content = _root;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Tab)
            return;
        if (e.Source is Visual v && v.FindAncestorOfType<ComboBox>() != null)
            return;

        if (e.Key == Key.Enter)
        {
            var mode = _selectedMode;
            if (mode?.PageType is null)
                return;
            var keyword = _searchBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
                return;
            OpenDownloadSearchTab(mode.PageType, keyword,
                string.Format(CommonLanguageManager.Instance.startPage_searchModeTitle.CurrentValue(),
                    mode.DisplayText));
            _searchBox.Text = null;
            _searchBox.SelectedItem = null;
            _searchBox.IsDropDownOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            var input = _searchBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                CycleSearchMode();
                e.Handled = true;
                return;
            }

            var match = _searchModes.FirstOrDefault(m => m.Matches(input.ToLowerInvariant()));
            if (match != null)
            {
                _selectedMode = match;
                _modeComboBox.SelectedItem = match;
                _searchBox.Text = null;
                _searchBox.SelectedItem = null;
                e.Handled = true;
            }
        }
    }

    private void CycleSearchMode()
    {
        if (_searchModes.Count == 0) return;

        var index = -1;
        for (var i = 0; i < _searchModes.Count; i++)
            if (ReferenceEquals(_searchModes[i], _selectedMode))
            {
                index = i;
                break;
            }

        var next = _searchModes[(index + 1) % _searchModes.Count];
        _selectedMode = next;
        _modeComboBox.SelectedItem = next;
    }

    private void OpenDownloadSearchTab(Type pageType, string keyword, string title)
    {
        if (TopLevel.GetTopLevel(this) is not TioTabWindowBase window)
            return;
        var page = new DownloadSearchTabPage(pageType, keyword, title);
        var tab = new TabEntry(window, page);
        window.CreateTab(tab);
        window.SelectTab(tab);
    }
}