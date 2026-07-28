using System.Collections.ObjectModel;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Module.Imaging;
using Portal.Services;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class FavoritesPage : UserControl
{
    public FavoritesPage()
    {
        InitializeComponent();
        DataContext = new FavoritesPageViewModel();
    }

    private void Item_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not FavoriteResource resource || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() is not null))
            return;
        FavoritesPageViewModel.OpenDetails(topLevel, resource);
        e.Handled = true;
    }

    private void Remove_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is FavoriteResource resource)
            ((FavoritesPageViewModel)DataContext!).Remove(resource);
        e.Handled = true;
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = (await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入收藏夹", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Portal 收藏夹") { Patterns = ["*.json"] }]
        })).FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(file)) ((FavoritesPageViewModel)DataContext!).Import(file);
    }

    private async void Edit_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FavoritesPageViewModel viewModel || viewModel.SelectedCollection is null)
            return;
        var result = await OverlayDialog.ShowCustomAsync<FavoriteCollectionEditDialog,
            FavoriteCollectionEditDialogViewModel, FavoriteCollectionEditResult>(
            new FavoriteCollectionEditDialogViewModel(viewModel.SelectedCollection), this.TryGetHostId(),
            new OverlayDialogOptions { Title = "编辑收藏夹", Buttons = DialogButton.None, CanResize = false });
        if (result is not null)
            viewModel.ApplyEdit(result);
    }

    private async void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FavoritesPageViewModel viewModel || viewModel.SelectedCollection is null)
            return;
        var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出收藏夹", SuggestedFileName = $"{viewModel.SelectedCollection.Name}.json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            viewModel.Export(path);
    }
}

public partial class FavoritesPageViewModel : ObservableObject
{
    private readonly FavoriteCollectionService _service = FavoriteCollectionService.Instance;
    public ObservableCollection<FavoriteCollection> Collections { get; } = [];
    public ObservableCollection<FavoriteResourceItem> Items { get; } = [];
    public IReadOnlyList<string> Editions { get; } = ["全部", "Java 版", "基岩版"];
    public IReadOnlyList<string> Kinds { get; } = ["全部类型", "模组", "整合包", "材质包", "光影包", "数据包", "存档"];
    [ObservableProperty] private FavoriteCollection? selectedCollection;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedEdition = "全部";
    [ObservableProperty] private string selectedKind = "全部类型";
    public bool ShowKindFilter => SelectedEdition != "基岩版";

    public FavoritesPageViewModel()
    {
        _service.Changed += (_, _) => RefreshCollections();
        RefreshCollections();
    }

    partial void OnSelectedCollectionChanged(FavoriteCollection? value)
    {
        RefreshItems();
    }
    partial void OnSearchTextChanged(string value) => RefreshItems();
    partial void OnSelectedEditionChanged(string value) { OnPropertyChanged(nameof(ShowKindFilter)); RefreshItems(); }
    partial void OnSelectedKindChanged(string value) => RefreshItems();

    [RelayCommand] private void AddCollection()
    {
        var collection = new FavoriteCollection { Name = "新收藏夹" };
        _service.Document.Collections.Add(collection);
        _service.Save();
        SelectedCollection = collection;
    }
    public void Remove(FavoriteResource resource) { _service.Remove(resource); RefreshItems(); }
    public void Import(string path) => _service.Import(path);
    public void Export(string path)
    {
        if (SelectedCollection is not null)
            _service.Export(SelectedCollection, path);
    }
    public void ApplyEdit(FavoriteCollectionEditResult result)
    {
        if (SelectedCollection is null) return;
        if (result.Delete)
        {
            _service.Document.Collections.Remove(SelectedCollection);
            if (_service.Document.Collections.Count == 0) _service.Document.Collections.Add(new FavoriteCollection());
        }
        else if (!string.IsNullOrWhiteSpace(result.Name))
            SelectedCollection.Name = result.Name;
        else
            return;
        _service.Save();
    }

    private void RefreshCollections()
    {
        var selectedId = SelectedCollection?.Id;
        Collections.Clear();
        foreach (var collection in _service.Document.Collections) Collections.Add(collection);
        SelectedCollection = Collections.FirstOrDefault(item => item.Id == selectedId) ?? Collections.FirstOrDefault();
        RefreshItems();
    }
    private void RefreshItems()
    {
        Items.Clear();
        if (SelectedCollection is null) return;
        foreach (var resource in SelectedCollection.Items.Where(Matches)) Items.Add(new FavoriteResourceItem(resource));
    }
    private bool Matches(FavoriteResource resource) =>
        (string.IsNullOrWhiteSpace(SearchText) || resource.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || resource.Summary.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) &&
        (SelectedEdition == "全部" || (SelectedEdition == "Java 版" ? resource.Edition == FavoriteEdition.Java : resource.Edition == FavoriteEdition.Bedrock)) &&
        (SelectedEdition == "基岩版" || SelectedKind == "全部类型" || resource.Kind.ToString() == KindFromDisplay(SelectedKind).ToString());
    private static JavaResourceKind KindFromDisplay(string value) => value switch
    {
        "模组" => JavaResourceKind.Mod, "整合包" => JavaResourceKind.Modpack, "材质包" => JavaResourceKind.ResourcePack, "光影包" => JavaResourceKind.ShaderPack,
        "数据包" => JavaResourceKind.DataPack, "存档" => JavaResourceKind.Save, _ => JavaResourceKind.Modpack
    };
    public static void OpenDetails(TopLevel topLevel, FavoriteResource resource)
    {
        if (resource.Kind == JavaResourceKind.Mod)
        {
            ModDetailsPage.Open(topLevel, new ModDetailsTarget(resource.Source, resource.ProjectId), resource.Name);
            return;
        }
        var definition = resource.Edition == FavoriteEdition.Bedrock
            ? BedrockResourceDefinitions.ResourcePack
            : resource.Kind switch
            {
                JavaResourceKind.Modpack => JavaResourceDefinitions.Modpack,
                JavaResourceKind.ResourcePack => JavaResourceDefinitions.ResourcePack,
                JavaResourceKind.ShaderPack => JavaResourceDefinitions.ShaderPack,
                JavaResourceKind.DataPack => JavaResourceDefinitions.DataPack,
                JavaResourceKind.Save => JavaResourceDefinitions.Save,
                _ => JavaResourceDefinitions.ResourcePack
            };
        var target = new JavaResourceDetailsTarget(definition, resource.Source, resource.ProjectId);
        if (resource.Edition == FavoriteEdition.Bedrock) BedrockResourceDetailsPage.Open(topLevel, target, resource.Name);
        else if (resource.Kind == JavaResourceKind.Modpack) ModpackDetailsPage.Open(topLevel, target, resource.Name);
        else if (resource.Kind == JavaResourceKind.ShaderPack) ShaderPackDetailsPage.Open(topLevel, target, resource.Name);
        else if (resource.Kind == JavaResourceKind.DataPack) DataPackDetailsPage.Open(topLevel, target, resource.Name);
        else if (resource.Kind == JavaResourceKind.Save) SaveDetailsPage.Open(topLevel, target, resource.Name);
        else ResourcePackDetailsPage.Open(topLevel, target, resource.Name);
    }
}

public sealed class FavoriteResourceItem : FavoriteResource
{
    public FavoriteResourceItem(FavoriteResource resource)
    {
        Id = resource.Id;
        Name = resource.Name;
        Summary = resource.Summary;
        IconUrl = resource.IconUrl;
        Edition = resource.Edition;
        Kind = resource.Kind;
        Source = resource.Source;
        ProjectId = resource.ProjectId;
    }

    public IAsyncImageLoader ImageLoader { get; } = new ModImageLoader();
    public string SourceText => $"{(Edition == FavoriteEdition.Java ? "Java 版" : "基岩版")}·{(Source == ModDetailsSource.CurseForge ? "CurseForge" : "Modrinth")}";
}
