using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Classes.Entries;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Operations.OpenFile;
using Portal.Core.Services;
using Portal.Module.DefaultPage;
using Portal.ViewModels;
using Portal.Views.Pages.DownloadPages;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using AutoCompleteBox = TioUi.Controls.AutoCompleteBox;
using Handler = Portal.Module.AggregatedSearch.Handler;

namespace Portal.Views.Pages;

[AggregatedSearchPage("起始页", "起始页", "NewTab")]
[DefaultPage("起始页")]
public partial class StartPage : DataUserControl, ITioTabPage
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

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "起始页",
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M96.5,160L96.5,309.5C96.5,326.5,103.2,342.8,115.2,354.8L307.2,546.8C332.2,571.8,372.7,571.8,397.7,546.8L547.2,397.3C572.2,372.3,572.2,331.8,547.2,306.8L355.2,114.8C343.2,102.7,327,96,310,96L160.5,96C125.2,96,96.5,124.7,96.5,160z M208.5,176C226.2,176 240.5,190.3 240.5,208 240.5,225.7 226.2,240 208.5,240 190.8,240 176.5,225.7 176.5,208 176.5,190.3 190.8,176 208.5,176z")
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        _viewModel.Dispose();
        DataContext = null;
    }

    private void StartPage_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _viewModel.SetRecentPlayWidth(e.NewSize.Width);
    }

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

        OpenDownloadSearchTab(mode.PageType, keyword, $"{mode.DisplayText}搜索");
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

        Handler.HandleAsync(entry, topLevel);
    }

    private void InstanceCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null))
            return;

        if (sender is Control { DataContext: MinecraftInstance instance } &&
            TopLevel.GetTopLevel(this) is { } topLevel)
            InstanceDetailPage.Open(instance, topLevel);
    }

    private void LaunchInstance_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not MinecraftInstance instance)
            return;

        _ = MinecraftLaunchService.LaunchAsync(instance, TopLevel.GetTopLevel(this),
            MinecraftLaunchOptionsFactory.Create(instance,
                logSession => MinecraftLogPage.Open(logSession, this.GetTopLevel())));
    }

    private async void CreateShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not MinecraftInstance instance)
            return;

        await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this),
            () => DesktopShortcutService.CreateAsync(instance));
    }

    private void FavoritedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var instance = (sender as Control)?.Tag as MinecraftInstance;
        if (instance == null || instance.Config == null) return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        _viewModel.ApplyFilterAndSort();
    }

    private void ButtonOpenInstance_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is null || sender.AsTopLevel() is not TioTabWindowBase window)
            return;

        var tab = new TabEntry(window, new InstancesPage());
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private async void RefreshInstance_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshInstancesAndRecentPlaysAsync();
    }

    private async void RefreshRecentPlays_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshInstancesAndRecentPlaysAsync();
    }

    private async Task RefreshInstancesAndRecentPlaysAsync()
    {
        InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        _viewModel.ApplyFilterAndSort();
        await RecentPlayListService.Instance.RefreshAsync();
    }

    private async void ImportModpack_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = this.GetTopLevel();
        var file = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入整合包",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("整合包") { Patterns = ["*.mrpack", "*.zip"] }]
        });
        var archivePath = file.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(archivePath))
            return;

        _ = ModpackDetailsPage.TryInstallFromPath(topLevel, archivePath);
    }

    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalOffset = 110,
            VerticalAnchor = VerticalPosition.Top
        };

        var result = await OverlayDialog
            .ShowCustomAsync<NewMinecraftFolder, NewMinecraftFolderViewModel, MinecraftFolderEntry>(
                new NewMinecraftFolderViewModel([
                    .. Data.ConfigEntry.MinecraftFolders.Select(x
                        => x.FolderPath)
                ]), this.TryGetHostId(), options);

        if (result == null) return;
        Data.ConfigEntry.MinecraftFolders.Add(result);
    }

    private async void ToDownload_Click(object? sender, RoutedEventArgs e)
    {
        var options = new OverlayDialogOptions
        {
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            CanResize = false,
            IsCloseButtonVisible = false
        };
        await OverlayDialog.ShowCustomAsync<CreateInstanceDialog, CreateInstanceDialogViewModel, bool>(
            new CreateInstanceDialogViewModel(), this.TryGetHostId(), options);
    }

    private void QuickPlay_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RecentPlayTarget target || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _ = MinecraftLaunchService.LaunchAsync(target.Instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(target.Instance,
                logSession => MinecraftLogPage.Open(logSession, topLevel)), target);
    }

    private async void RecentPlayCreateShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RecentPlayItem item)
            return;

        var target = item.Target;
        await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this),
            () => DesktopShortcutService.CreateAsync(target.Instance, target));
    }

    private async void RecentPlayItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null))
            return;

        if (sender is not Control { DataContext: RecentPlayItem item })
            return;

        var target = item.Target;
        if (target.Type != RecentPlayTargetType.World)
            return;

        var saveService = new WorldSaveService();
        var worldInfo = await saveService.ReadAsync(target.Instance, target.Id);
        if (worldInfo == null)
            return;

        await OverlayDialog.ShowCustomAsync<WorldSaveDetails, WorldSaveDetailsViewModel, object>(
            new WorldSaveDetailsViewModel(worldInfo, target.Instance), this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Mode = DialogMode.None,
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                IsCloseButtonVisible = true,
                CloseBtnMargin = new Thickness(0, 12, 12, 0)
            });
    }

    private void RecentPlayFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is RecentPlayItem item)
            _viewModel.ToggleRecentPlayFavorite(item);
    }

    private void BlockInstance_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not MinecraftInstance instance)
            return;

        BlockListService.Instance.ToggleInstanceBlock(instance);
    }

    private void BlockRecentPlay_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RecentPlayTarget target)
            return;

        BlockListService.Instance.ToggleRecentPlayBlock(target);
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

public partial class StartPageViewModel : InstanceListViewModelBase
{
    private readonly RecentPlayListService _recentPlayListService = RecentPlayListService.Instance;
    private List<RecentPlayItem> _allRecentPlays = [];
    private bool _isDisposed;
    private int _recentPlayCapacity = 1;

    public StartPageViewModel()
    {
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.SortType == Data.ConfigEntry.DefaultInstanceSortType);
        SelectedSearchMode = SearchModes[0];
        _recentPlayListService.Refreshed += OnRecentPlaysRefreshed;
        UpdateRecentPlays(_recentPlayListService.Items);
    }

    public static IReadOnlyList<SearchMode> DefaultSearchModes { get; } =
    [
        new("本地", null, [
                "本地", "bd", "local",
                "本机", "本地文件", "本机文件", "local file", "localfiles",
                "本机资源", "我的电脑", "本地存档", "本地模组"
            ],
            "F1 M640,640z M0,0z M541.9,139.5C546.4,127.7 543.6,114.3 534.7,105.4 525.8,96.5 512.4,93.6 500.6,98.2L84.6,258.2C71.9,263 63.7,275.2 64,288.7 64.3,302.2 73.1,314.1 85.9,318.3L262.7,377.2 321.6,554C325.9,566.8 337.7,575.6 351.2,575.9 364.7,576.2 376.9,568 381.8,555.4L541.8,139.4z"),

        new("整合包", typeof(ModpackSearchPage), [
                "整合包", "zhb",
                "懒人包", "一键包", "整合", "modpack", "mod packs", "整合模组包"
            ],
            "F1 M640,640z M0,0z M560.3,301.2C570.7,313 588.6,315.6 602.1,306.7 616.8,296.9 620.8,277 611,262.3L563,190.3C560.2,186.1,556.4,182.6,551.9,180.1L351.4,68.7C332.1,58,308.6,58,289.2,68.7L88.8,180C83.4,183,79.1,187.4,76.2,192.8L27.7,282.7C15.1,306.1,23.9,335.2,47.3,347.8L80.3,365.5 80.3,418.8C80.3,441.8,92.7,463.1,112.7,474.5L288.7,574.2C308.3,585.3,332.2,585.3,351.8,574.2L527.8,474.5C547.9,463.1,560.2,441.9,560.2,418.8L560.2,301.3z M320.3,291.4L170.2,208 320.3,124.6 470.4,208 320.3,291.4z M278.8,341.6L257.5,387.8 91.7,299 117.1,251.8 278.8,341.6z"),

        new("模组", typeof(ModSearchPage), [
                "模组", "mods", "mod", "mz",
                "模块", "插件", "mod文件", "mod组件", "modification", "md", "mokuai"
            ],
            "F1 M640,640z M0,0z M224,32C241.7,32,256,46.3,256,64L256,160 384,160 384,64C384,46.3 398.3,32 416,32 433.7,32 448,46.3 448,64L448,160 512,160C529.7,160 544,174.3 544,192 544,209.7 529.7,224 512,224L512,288C512,383.1,442.8,462.1,352,477.3L352,544C352,561.7 337.7,576 320,576 302.3,576 288,561.7 288,544L288,477.3C197.2,462.1,128,383.1,128,288L128,224C110.3,224 96,209.7 96,192 96,174.3 110.3,160 128,160L192,160 192,64C192,46.3,206.3,32,224,32z"),

        new("资源包", typeof(ResourcePackSearchPage), [
                "资源包", "材质包", "resource pack", "resourcepack", "zyb", "rp",
                "贴图包", "纹理包", "材质", "texture", "texture pack", "tp", "czb", "zy", "wzbao"
            ],
            "F1 M1024,1024z M0,0z M173.582629,408.517486L463.36,555.176229C481.528686,564.487315 503.237486,569.358629 526.723657,569.358629 548.878628,569.358629 570.141257,563.594972 590.08,555.176229L879.857371,408.5248C896.256,399.213714 906.890971,381.4912 906.890971,362.883657 906.890971,342.944914 896.256,326.546286 879.857371,317.242514L590.08,170.583771C570.141257,159.9488 547.9936,154.185142 526.723657,154.185143 505.453714,154.185143 483.298743,159.9488 463.36,168.367543L173.582629,315.904C157.191315,326.538971 146.556343,342.9376 146.556343,362.876343 146.117486,381.483886 157.191314,399.2064 173.582629,408.517486z M564.823771,649.479314C541.344914,660.114285,514.318628,660.114285,492.163657,649.479314L204.156343,500.831086C185.987657,490.196115 162.062629,497.283657 151.427657,515.006171 140.792686,532.736 147.887543,557.099885 165.610057,567.742171L455.387429,716.383086C477.5424,727.018057 501.028572,732.7744 525.838629,732.7744 549.317486,732.7744 572.796343,727.018057 598.498743,716.383086L888.276114,567.727543C906.4448,557.092572 911.762285,533.613714 901.127314,514.998857 890.938514,496.837486 868.337371,489.303771 849.729829,500.823771L564.823771,649.479314z M564.823771,815.5648C541.344914,826.199771,514.318628,826.199771,492.163657,815.5648L204.156343,669.1328C185.987657,658.497829 162.062629,665.585371 151.427657,683.307886 140.792686,701.476572 147.887543,725.4016 165.610057,736.036571L455.387429,884.6848C477.5424,895.319771 501.028572,901.076114 525.838629,901.076114 552.864915,901.076114 576.351086,895.319771 598.498743,884.692114L888.276114,736.036571C906.4448,725.4016 911.762285,701.915428 901.127314,683.300571 891.823543,663.369142 868.337371,657.605485 849.729829,666.916571L564.823771,815.5648z"),

        new("光影包", typeof(ShaderPackSearchPage), [
                "光影包", "shader", "shaders",
                "光影", "着色器", "光影文件", "光影材质", "shader pack", "sd", "gy", "gyb",
                "着色包", "光影补丁"
            ],
            "F1 M640,640z M0,0z M420.9,448C428.2,425.7 442.8,405.5 459.3,388.1 492,353.7 512,307.2 512,256 512,150 426,64 320,64 214,64 128,150 128,256 128,307.2 148,353.7 180.7,388.1 197.2,405.5 211.9,425.7 219.1,448L420.8,448z M416,496L224,496 224,512C224,556.2,259.8,592,304,592L336,592C380.2,592,416,556.2,416,512L416,496z M312,176C272.2,176 240,208.2 240,248 240,261.3 229.3,272 216,272 202.7,272 192,261.3 192,248 192,181.7 245.7,128 312,128 325.3,128 336,138.7 336,152 336,165.3 325.3,176 312,176z"),

        new("数据包", typeof(DataPackSearchPage), [
                "数据包", "datapack", "data pack",
                "数据", "数据文件", "dp", "sjb", "data package", "mc数据包"
            ],
            "F1 M640,640z M0,0z M256,144C256,117.5,277.5,96,304,96L336,96C362.5,96,384,117.5,384,144L384,496C384,522.5,362.5,544,336,544L304,544C277.5,544,256,522.5,256,496L256,144z M64,336C64,309.5,85.5,288,112,288L144,288C170.5,288,192,309.5,192,336L192,496C192,522.5,170.5,544,144,544L112,544C85.5,544,64,522.5,64,496L64,336z M496,160L528,160C554.5,160,576,181.5,576,208L576,496C576,522.5,554.5,544,528,544L496,544C469.5,544,448,522.5,448,496L448,208C448,181.5,469.5,160,496,160z"),

        new("存档", typeof(SaveSearchPage), [
                "存档", "saves", "save", "world",
                "世界", "存档文件", "游戏存档", "存档记录", "存档世界", "cun", "cd", "world save"
            ],
            "F1 M640,640z M0,0z M119.7,263.7L150.6,294.6C156.6,300.6,164.7,304,173.2,304L194.7,304C203.2,304,211.3,307.4,217.3,313.4L246.6,342.7C252.6,348.7,256,356.8,256,365.3L256,402.8C256,411.3,259.4,419.4,265.4,425.4L278.7,438.7C284.7,444.7,288.1,452.8,288.1,461.3L288.1,480C288.1,497.7 302.4,512 320.1,512 337.8,512 352.1,497.7 352.1,480L352.1,477.3C352.1,468.8,355.5,460.7,361.5,454.7L406.8,409.4C412.8,403.4,416.2,395.3,416.2,386.8L416.2,352.1C416.2,334.4,401.9,320.1,384.2,320.1L301.5,320.1C293,320.1,284.9,316.7,278.9,310.7L262.9,294.7C258.7,290.5 256.3,284.7 256.3,278.7 256.3,266.2 266.4,256.1 278.9,256.1L313.6,256.1C326.1,256.1 336.2,246 336.2,233.5 336.2,227.5 333.8,221.7 329.6,217.5L309.9,197.8C306,194 304,189.1 304,184 304,178.9 306,174 309.7,170.3L327,153C332.8,147.2 336.1,139.3 336.1,131.1 336.1,123.9 333.7,117.4 329.7,112.2 326.5,112.1 323.3,112 320.1,112 224.7,112 144.4,176.2 119.8,263.7z M528,320C528,285.4 519.6,252.8 504.6,224.2 498.2,225.1 491.9,228.1 486.7,233.3L473.3,246.7C467.3,252.7,463.9,260.8,463.9,269.3L463.9,304C463.9,321.7,478.2,336,495.9,336L520,336C522.5,336 525,335.7 527.3,335.2 527.7,330.2 527.8,325.1 527.8,320z M64,320C64,178.6 178.6,64 320,64 461.4,64 576,178.6 576,320 576,461.4 461.4,576 320,576 178.6,576 64,461.4 64,320z"),

        new("基岩包", typeof(BedrockResourcePackSearchPage), [
                "基岩包", "bedrock", "基岩", "jy", "jyb",
                "基岩版材质", "基岩资源", "BE", "bedrock pack", "基岩材质包", "jyzyb", "基岩光影"
            ],
            "F1 M640,640z M0,0z M348,62.7C330.7,52.7,309.3,52.7,292,62.7L207.8,111.3C190.5,121.3,179.8,139.8,179.8,159.8L179.8,261.7 91.5,312.7C74.2,322.7,63.5,341.2,63.5,361.2L63.5,458.5C63.5,478.5,74.2,497,91.5,507L175.8,555.6C193.1,565.6,214.5,565.6,231.8,555.6L320.1,504.6 408.4,555.6C425.7,565.6,447.1,565.6,464.4,555.6L548.5,507C565.8,497,576.5,478.5,576.5,458.5L576.5,361.2C576.5,341.2,565.8,322.7,548.5,312.7L460.2,261.7 460.2,159.8C460.2,139.8,449.5,121.3,432.2,111.3L348,62.7z M296,356.6L296,463.1 207.7,514.1C206.5,514.8,205.1,515.2,203.7,515.2L203.7,409.9 296,356.6z M527.4,357.2C528.1,358.4,528.5,359.8,528.5,361.2L528.5,458.5C528.5,461.4,527,464,524.5,465.4L440.2,514C439,514.7,437.6,515.1,436.2,515.1L436.2,409.8 527.4,357.2z M412.3,159.8L412.3,261.7 320,315 320,208.5 411.2,155.9C411.9,157.1,412.3,158.5,412.3,159.9z")
    ];

    public IReadOnlyList<SearchMode> SearchModes => DefaultSearchModes;

    [ObservableProperty] public partial SearchMode? SelectedSearchMode { get; set; }

    public string SearchPlaceholder =>
        SelectedSearchMode?.PageType is null ? "搜索实例、存档、服务器、页面" : $"搜索{SelectedSearchMode.DisplayText}";

    public ObservableCollection<RecentPlayItem> RecentPlays { get; } = [];
    public bool HasRecentPlays => _allRecentPlays.Count > 0;
    public bool CanExpandRecentPlays => GetVisibleRecentPlays().Count > _recentPlayCapacity;

    public string ToggleRecentPlaysText =>
        Data.ConfigEntry.StartPageRecentPlaysExpanded ? "收起" : $"展开全部 ({GetVisibleRecentPlays().Count})";

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

    private List<RecentPlayItem> GetVisibleRecentPlays()
    {
        return BlockListService.Instance.ShowBlockedRecentPlays
            ? _allRecentPlays
            : _allRecentPlays.Where(item => !BlockListService.Instance.IsRecentPlayBlocked(item.Target)).ToList();
    }

    protected override void RefreshRecentPlaysForBlockList()
    {
        foreach (var item in _allRecentPlays)
            item.RefreshBlockState();
        ApplyRecentPlayCapacity();
    }

    protected override IEnumerable<RecentPlayTarget> GetRecentPlayTargets()
    {
        return _allRecentPlays.Select(item => item.Target);
    }

    private void UpdateRecentPlays(IEnumerable<RecentPlayTarget> targets)
    {
        RecentPlays.Clear();
        DisposeRecentPlays();
        _allRecentPlays = [.. targets.Select(target => new RecentPlayItem(target))];
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
        var take = Data.ConfigEntry.StartPageRecentPlaysExpanded ? visiblePlays.Count : _recentPlayCapacity;
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
        Data.ConfigEntry.StartPageRecentPlaysExpanded = !Data.ConfigEntry.StartPageRecentPlaysExpanded;
        ApplyRecentPlayCapacity();
    }

    public void ToggleRecentPlayFavorite(RecentPlayItem item)
    {
        item.ToggleFavorite();
        SortRecentPlays();
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

    private void DisposeRecentPlays()
    {
        foreach (var item in _allRecentPlays)
            item.Dispose();
        _allRecentPlays.Clear();
    }
}