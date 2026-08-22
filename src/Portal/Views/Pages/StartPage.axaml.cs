using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module;
using Portal.Module.DefaultPage;
using Portal.Localization;
using Portal.ViewModels;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common.Extensions;
using AutoCompleteBox = TioUi.Controls.AutoCompleteBox;

namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_startPage", "pages_startPagePath", "NewTab")]
[DefaultPage("pages_startPage")]
public partial class StartPage : InstanceListPageBase
{
    private readonly StartPageViewModel _viewModel;
    private bool _isInitialized;
    private bool _suppressSearchPopulate;

    public StartPage()
    {
        InitializeComponent();
        _viewModel = new StartPageViewModel();
        DataContext = _viewModel;
        SearchBox.AddHandler(KeyDownEvent, SearchBox_OnKeyDown, RoutingStrategies.Bubble,
            true);
        Loaded += (_, _) =>
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            _viewModel.ApplyFilterAndSort();
        };
    }

    public override PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.startPage_pageTitle.CurrentValue(),
        IconGlyph = "\ue619", IconFont = IconResources.FontFamilyName
    };

    protected override InstanceListViewModelBase PageViewModel => _viewModel;

    private void SearchBox_OnPopulating(object? sender, PopulatingEventArgs e)
    {
        if (_suppressSearchPopulate)
        {
            _suppressSearchPopulate = false;
            e.Cancel = true;
            return;
        }

        if (_viewModel.SelectedSearchMode?.PageType is not null)
        {
            e.Cancel = true;
            return;
        }

        if (sender is AutoCompleteBox box)
            box.ItemsSource = Searcher.Search(e.Parameter ?? string.Empty);
    }

    private void SearchBox_OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not AutoCompleteBox box)
            return;

        if (_viewModel.SelectedSearchMode?.PageType is not null)
        {
            box.IsDropDownOpen = false;
            return;
        }

        box.ItemsSource = Searcher.Search(box.Text ?? string.Empty);
    }

    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is Visual visual && visual.FindAncestorOfType<ComboBox>() != null)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                HandleSearchEnter(e);
                break;
            case Key.Tab:
                HandleSearchTab(e);
                break;
        }
    }

    private void HandleSearchEnter(KeyEventArgs e)
    {
        var mode = _viewModel.SelectedSearchMode;
        if (mode?.PageType is null)
            return;

        var keyword = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
            return;

        OpenDownloadSearchTab(mode.PageType, keyword,
            string.Format(CommonLanguageManager.Instance.startPage_searchModeTitle.CurrentValue(),
                mode.DisplayText));
        ClearSearchBox();
        e.Handled = true;
    }

    private void HandleSearchTab(KeyEventArgs e)
    {
        var input = SearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            _viewModel.CycleSearchMode();
            ClearSearchBox();
            e.Handled = true;
            return;
        }

        var keyword = input.ToLowerInvariant();
        var match = _viewModel.SearchModes.FirstOrDefault(mode => mode.Matches(keyword));
        if (match == null)
            return;

        _viewModel.SelectedSearchMode = match;
        ClearSearchBox();
        e.Handled = true;
    }

    private void ClearSearchBox()
    {
        if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            _suppressSearchPopulate = true;
            SearchBox.Text = null;
        }

        SearchBox.SelectedItem = null;
        SearchBox.IsDropDownOpen = false;
    }

    private void OpenDownloadSearchTab(Type pageType, string keyword, string title)
    {
        if (this.GetTopLevel() is not TioTabWindowBase window)
            return;

        var page = new DownloadSearchTabPage(pageType, keyword, title);
        var tab = new TabEntry(window, page);
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private void SearchBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not AggregatedSearchEntry entry)
            return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        if (sender is AutoCompleteBox box)
        {
            _suppressSearchPopulate = true;
            box.SelectedItem = null;
            box.Text = null;
            box.IsDropDownOpen = false;
        }

        AggregatedSearchHandler.HandleAsync(entry, topLevel);
    }
}

public sealed record SearchMode(
    string DisplayText,
    Type? PageType,
    IReadOnlyList<string> Aliases,
    string? IconData = null)
{
    public bool Matches(string keyword)
    {
        return Aliases.Contains(keyword);
    }
}

public partial class StartPageViewModel : RecentPlaysViewModelBase
{
    public StartPageViewModel()
    {
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
        SelectedSearchMode = SearchModes[0];
    }

    public static IReadOnlyList<SearchMode> DefaultSearchModes { get; } =
    [
        new(CommonLanguageManager.Instance.startPage_searchLocal.CurrentValue(), null, [
                "本地", "bd", "local",
                "本机", "本地文件", "本机文件", "local file", "localfiles",
                "本机资源", "我的电脑", "本地存档", "本地模组"
            ],
            "\ue613"),

        new(CommonLanguageManager.Instance.startPage_searchModpack.CurrentValue(), typeof(ModpackSearchPage), [
                "整合包", "zhb",
                "懒人包", "一键包", "整合", "modpack", "mod packs", "整合模组包"
            ],
            "\ue611"),

        new(CommonLanguageManager.Instance.startPage_searchMod.CurrentValue(), typeof(ModSearchPage), [
                "模组", "mods", "mod", "mz",
                "模块", "插件", "mod文件", "mod组件", "modification", "md", "mokuai"
            ],
            "\ue630"),

        new(CommonLanguageManager.Instance.startPage_searchResourcePack.CurrentValue(), typeof(ResourcePackSearchPage), [
                "资源包", "材质包", "resource pack", "resourcepack", "zyb", "rp",
                "贴图包", "纹理包", "材质", "texture", "texture pack", "tp", "czb", "zy", "wzbao"
            ],
            "\ue64d"),

        new(CommonLanguageManager.Instance.startPage_searchShaderPack.CurrentValue(), typeof(ShaderPackSearchPage), [
                "光影包", "shader", "shaders",
                "光影", "着色器", "光影文件", "光影材质", "shader pack", "sd", "gy", "gyb",
                "着色包", "光影补丁"
            ],
            "\ue63e"),

        new(CommonLanguageManager.Instance.startPage_searchDataPack.CurrentValue(), typeof(DataPackSearchPage), [
                "数据包", "datapack", "data pack",
                "数据", "数据文件", "dp", "sjb", "data package", "mc数据包"
            ],
            "\ue63b"),

        new(CommonLanguageManager.Instance.startPage_searchSave.CurrentValue(), typeof(SaveSearchPage), [
                "存档", "saves", "save", "world",
                "世界", "存档文件", "游戏存档", "存档记录", "存档世界", "cun", "cd", "world save"
            ],
            "\ue629"),

        new(CommonLanguageManager.Instance.startPage_searchBedrockPack.CurrentValue(), typeof(BedrockResourcePackSearchPage), [
                "基岩包", "bedrock", "基岩", "jy", "jyb",
                "基岩版材质", "基岩资源", "BE", "bedrock pack", "基岩材质包", "jyzyb", "基岩光影"
            ],
            "\ue638")
    ];

    public IReadOnlyList<SearchMode> SearchModes => DefaultSearchModes;

    [ObservableProperty] public partial SearchMode? SelectedSearchMode { get; set; }

    public string SearchPlaceholder =>
        SelectedSearchMode?.PageType is null
            ? CommonLanguageManager.Instance.startPage_searchPlaceholder.CurrentValue()
            : string.Format(CommonLanguageManager.Instance.startPage_searchPlaceholderMode.CurrentValue(),
                SelectedSearchMode.DisplayText);

    protected override bool RecentPlaysExpanded
    {
        get => Data.ConfigEntry.StartPageRecentPlaysExpanded;
        set => Data.ConfigEntry.StartPageRecentPlaysExpanded = value;
    }

    partial void OnSelectedSearchModeChanged(SearchMode? value)
    {
        OnPropertyChanged(nameof(SearchPlaceholder));
    }

    public void CycleSearchMode()
    {
        if (SelectedSearchMode is null)
        {
            SelectedSearchMode = SearchModes[0];
            return;
        }

        var index = -1;
        for (var i = 0; i < SearchModes.Count; i++)
            if (ReferenceEquals(SearchModes[i], SelectedSearchMode))
            {
                index = i;
                break;
            }

        SelectedSearchMode = SearchModes[(index + 1) % SearchModes.Count];
    }
}
