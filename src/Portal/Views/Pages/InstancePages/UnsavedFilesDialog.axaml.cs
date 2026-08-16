using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.InstancePages;

public partial class UnsavedFilesDialog : UserControl
{
    public UnsavedFilesDialog()
    {
        InitializeComponent();
    }
}

public enum UnsavedFilesAction
{
    Cancel,
    Discard,
    Save
}

public partial class UnsavedFilesDialogViewModel(string message) : ObservableObject, IDialogContext
{
    public string Message { get; } = message;

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    [RelayCommand]
    private void Save()
    {
        RequestClose?.Invoke(this, UnsavedFilesAction.Save);
    }

    [RelayCommand]
    private void Discard()
    {
        RequestClose?.Invoke(this, UnsavedFilesAction.Discard);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, UnsavedFilesAction.Cancel);
    }
}