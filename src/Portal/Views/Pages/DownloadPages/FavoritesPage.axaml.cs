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
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Module.Imaging;
using Portal.Localization;
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
            (sender as Control)?.DataContext is not FavoriteResource resource ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() is not null))
            return;
        FavoritesPageViewModel.OpenDetails(topLevel, resource);
        e.Handled = true;
    }

    private void Favorite_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is FavoriteResource resource)
            ((FavoritesPageViewModel)DataContext!).Remove(resource);
        e.Handled = true;
    }

    private async void Download_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is FavoriteResource resource && TopLevel.GetTopLevel(this) is { } topLevel)
            await FavoritesPageViewModel.QuickDownloadAsync(topLevel, resource);
        e.Handled = true;
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = (await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = CommonLanguageManager.Instance.favorite_import.CurrentValue(), AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(CommonLanguageManager.Instance.favorite_portalCollection.CurrentValue())
                {
                    Patterns = ["*.json"]
                }
            ]
        })).FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(file)) ((FavoritesPageViewModel)DataContext!).Import(file);
    }

    private async void Rename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FavoritesPageViewModel viewModel || viewModel.SelectedCollection is null)
            return;
        var result = await OverlayDialog.ShowCustomAsync<FavoriteCollectionEditDialog,
            FavoriteCollectionEditDialogViewModel, FavoriteCollectionEditResult>(
            new FavoriteCollectionEditDialogViewModel(viewModel.SelectedCollection), this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.favorite_edit.CurrentValue(), Buttons = DialogButton.None,
                CanResize = false
            });
        if (result is not null)
            viewModel.ApplyEdit(result);
    }

    private void Delete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FavoritesPageViewModel viewModel)
            viewModel.DeleteSelectedCollection();
    }

    private async void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FavoritesPageViewModel viewModel || viewModel.SelectedCollection is null)
            return;
        var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = CommonLanguageManager.Instance.favorite_export.CurrentValue(),
            SuggestedFileName = $"{viewModel.SelectedCollection.Name}.json",
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
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private FavoriteCollection? selectedCollection;
    [ObservableProperty] private string selectedEdition = CommonLanguageManager.Instance.mod_all.CurrentValue();
    [ObservableProperty] private string selectedKind = CommonLanguageManager.Instance.favorite_allKinds.CurrentValue();

    public FavoritesPageViewModel()
    {
        _service.Changed += (_, _) => RefreshCollections();
        RefreshCollections();
    }

    public ObservableCollection<FavoriteCollection> Collections { get; } = [];
    public ObservableCollection<FavoriteResourceItem> Items { get; } = [];
    public IReadOnlyList<string> Editions { get; } =
    [
        CommonLanguageManager.Instance.mod_all.CurrentValue(),
        CommonLanguageManager.Instance.launch_javaEdition.CurrentValue(),
        CommonLanguageManager.Instance.launch_bedrockEdition.CurrentValue()
    ];
    public IReadOnlyList<string> Kinds { get; } =
    [
        CommonLanguageManager.Instance.favorite_allKinds.CurrentValue(),
        CommonLanguageManager.Instance.favorite_kindMod.CurrentValue(),
        CommonLanguageManager.Instance.favorite_kindModpack.CurrentValue(),
        CommonLanguageManager.Instance.favorite_kindResourcePack.CurrentValue(),
        CommonLanguageManager.Instance.favorite_kindShaderPack.CurrentValue(),
        CommonLanguageManager.Instance.favorite_kindDataPack.CurrentValue(),
        CommonLanguageManager.Instance.favorite_kindSave.CurrentValue()
    ];
    public bool ShowKindFilter => SelectedEdition != CommonLanguageManager.Instance.launch_bedrockEdition.CurrentValue();

    partial void OnSelectedCollectionChanged(FavoriteCollection? value)
    {
        RefreshItems();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshItems();
    }

    partial void OnSelectedEditionChanged(string value)
    {
        OnPropertyChanged(nameof(ShowKindFilter));
        RefreshItems();
    }

    partial void OnSelectedKindChanged(string value)
    {
        RefreshItems();
    }

    [RelayCommand]
    private void AddCollection()
    {
        var collection = new FavoriteCollection { Name = CommonLanguageManager.Instance.favorite_newCollection.CurrentValue() };
        _service.Document.Collections.Add(collection);
        _service.Save();
        SelectedCollection = collection;
    }

    public void Remove(FavoriteResource resource)
    {
        _service.Remove(resource);
        RefreshItems();
    }

    public void Import(string path)
    {
        _service.Import(path);
    }

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
        {
            SelectedCollection.Name = result.Name;
        }
        else
        {
            return;
        }

        _service.Save();
    }

    public void DeleteSelectedCollection()
    {
        if (SelectedCollection is null) return;
        _service.Document.Collections.Remove(SelectedCollection);
        if (_service.Document.Collections.Count == 0) _service.Document.Collections.Add(new FavoriteCollection());
        _service.Save();
        RefreshCollections();
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

    private bool Matches(FavoriteResource resource)
    {
        var javaEdition = CommonLanguageManager.Instance.launch_javaEdition.CurrentValue();
        var bedrockEdition = CommonLanguageManager.Instance.launch_bedrockEdition.CurrentValue();
        var allKinds = CommonLanguageManager.Instance.favorite_allKinds.CurrentValue();
        return (string.IsNullOrWhiteSpace(SearchText) ||
                resource.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                resource.Summary.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) &&
               (SelectedEdition == CommonLanguageManager.Instance.mod_all.CurrentValue() ||
                (SelectedEdition == javaEdition
                    ? resource.Edition == FavoriteEdition.Java
                    : resource.Edition == FavoriteEdition.Bedrock)) &&
               (SelectedEdition == bedrockEdition || SelectedKind == allKinds ||
                resource.Kind.ToString() == KindFromDisplay(SelectedKind).ToString());
    }

    private static ResourceKind KindFromDisplay(string value)
    {
        var mod = CommonLanguageManager.Instance.favorite_kindMod.CurrentValue();
        var modpack = CommonLanguageManager.Instance.favorite_kindModpack.CurrentValue();
        var resourcePack = CommonLanguageManager.Instance.favorite_kindResourcePack.CurrentValue();
        var shaderPack = CommonLanguageManager.Instance.favorite_kindShaderPack.CurrentValue();
        var dataPack = CommonLanguageManager.Instance.favorite_kindDataPack.CurrentValue();
        var save = CommonLanguageManager.Instance.favorite_kindSave.CurrentValue();
        return value switch
        {
            _ when value == mod => ResourceKind.Mod,
            _ when value == modpack => ResourceKind.Modpack,
            _ when value == resourcePack => ResourceKind.ResourcePack,
            _ when value == shaderPack => ResourceKind.ShaderPack,
            _ when value == dataPack => ResourceKind.DataPack,
            _ when value == save => ResourceKind.Save,
            _ => ResourceKind.Modpack
        };
    }

    public static void OpenDetails(TopLevel topLevel, FavoriteResource resource)
    {
        if (resource.Kind == ResourceKind.Mod)
        {
            ResourceDetailsPage.Open(topLevel,
                new ResourceDetailsTarget(ResourceDefinitions.Mod, resource.Source, resource.ProjectId),
                resource.Name);
            return;
        }

        var definition = resource.Edition == FavoriteEdition.Bedrock
            ? BedrockResourceDefinitions.ResourcePack
            : resource.Kind switch
            {
                ResourceKind.Modpack => ResourceDefinitions.Modpack,
                ResourceKind.ResourcePack => ResourceDefinitions.ResourcePack,
                ResourceKind.ShaderPack => ResourceDefinitions.ShaderPack,
                ResourceKind.DataPack => ResourceDefinitions.DataPack,
                ResourceKind.Save => ResourceDefinitions.Save,
                _ => ResourceDefinitions.ResourcePack
            };
        var target = new ResourceDetailsTarget(definition, resource.Source, resource.ProjectId);
        ResourceDetailsPage.Open(topLevel, target, resource.Name);
    }

    public static async Task QuickDownloadAsync(TopLevel topLevel, FavoriteResource resource)
    {
        if (resource.Edition == FavoriteEdition.Bedrock)
        {
            var bedrockDefinition = resource.Kind switch
            {
                ResourceKind.BedrockBehaviorPack => BedrockResourceDefinitions.BehaviorPack,
                ResourceKind.BedrockResourcePack => BedrockResourceDefinitions.ResourcePack,
                ResourceKind.BedrockWorld => BedrockResourceDefinitions.World,
                ResourceKind.BedrockWorldTemplate => BedrockResourceDefinitions.WorldTemplate,
                _ => null
            };
            if (bedrockDefinition is not null)
                await BedrockResourceDownload.QuickDownloadAsync(topLevel,
                    new ResourceDetailsTarget(bedrockDefinition, resource.Source, resource.ProjectId));
            return;
        }

        if (resource.Kind == ResourceKind.Modpack)
        {
            OpenDetails(topLevel, resource);
            return;
        }

        if (resource.Kind == ResourceKind.Mod)
        {
            await ResourceDownload.QuickDownloadAsync(topLevel,
                new ResourceDetailsTarget(ResourceDefinitions.Mod, resource.Source, resource.ProjectId));
            return;
        }

        var definition = resource.Kind switch
        {
            ResourceKind.ResourcePack => ResourceDefinitions.ResourcePack,
            ResourceKind.ShaderPack => ResourceDefinitions.ShaderPack,
            ResourceKind.DataPack => ResourceDefinitions.DataPack,
            ResourceKind.Save => ResourceDefinitions.Save,
            _ => null
        };
        if (definition is not null)
            await ResourceDownload.QuickDownloadAsync(topLevel,
                new ResourceDetailsTarget(definition, resource.Source, resource.ProjectId));
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

    public string SourceText =>
        string.Format(CommonLanguageManager.Instance.favorite_sourceText.CurrentValue(),
            Edition == FavoriteEdition.Java
                ? CommonLanguageManager.Instance.launch_javaEdition.CurrentValue()
                : CommonLanguageManager.Instance.launch_bedrockEdition.CurrentValue(),
            Source == ModDetailsSource.CurseForge ? "CurseForge" : "Modrinth");
}