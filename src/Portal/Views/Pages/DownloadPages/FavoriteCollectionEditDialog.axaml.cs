using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Services;
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
}

public sealed record FavoriteCollectionEditResult(string? Name, bool Delete);

public partial class FavoriteCollectionEditDialogViewModel(FavoriteCollection collection)
    : ObservableObject, IDialogContext
{
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
}