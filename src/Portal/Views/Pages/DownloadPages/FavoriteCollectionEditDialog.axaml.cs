using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Services;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

public partial class FavoriteCollectionEditDialog : UserControl
{
    public FavoriteCollectionEditDialog()
    {
        InitializeComponent();
    }

    private void Complete_OnClick(object? sender, RoutedEventArgs e)
    {
        ((FavoriteCollectionEditDialogViewModel)DataContext!).Complete();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        ((FavoriteCollectionEditDialogViewModel)DataContext!).Cancel();
    }

    private void Delete_OnClick(object? sender, RoutedEventArgs e)
    {
        ((FavoriteCollectionEditDialogViewModel)DataContext!).Delete();
    }

    private async void Share_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "分享收藏夹", SuggestedFileName = "Portal 收藏夹.json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) ((FavoriteCollectionEditDialogViewModel)DataContext!).Export(path);
    }
}

public sealed record FavoriteCollectionEditResult(string? Name, bool Delete);

public partial class FavoriteCollectionEditDialogViewModel(FavoriteCollection collection)
    : ObservableObject, IDialogContext
{
    private readonly FavoriteCollection _collection = collection;
    [ObservableProperty] private string draftName = collection.Name;

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    public void Complete()
    {
        RequestClose?.Invoke(this, new FavoriteCollectionEditResult(DraftName.Trim(), false));
    }

    public void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }

    public void Delete()
    {
        RequestClose?.Invoke(this, new FavoriteCollectionEditResult(null, true));
    }

    public void Export(string path)
    {
        FavoriteCollectionService.Instance.Export(_collection, path);
    }
}