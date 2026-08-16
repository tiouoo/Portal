using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Interfaces;
using Portal.Core.Minecraft.Models;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

public partial class LoaderVersionDialog : UserControl
{
    public LoaderVersionDialog()
    {
        InitializeComponent();
    }

    private void Confirm_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as LoaderVersionDialogViewModel)?.Confirm();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as LoaderVersionDialogViewModel)?.Cancel();
    }
}

public sealed record LoaderVersionItem(LoaderKind Kind, IInstallEntry Entry, string Version)
{
    public string DisplayName => $"{Kind} {Version}";
}

public partial class LoaderVersionDialogViewModel : ObservableObject, IDialogContext
{
    public LoaderVersionDialogViewModel(IReadOnlyList<LoaderVersionItem> versions)
    {
        Versions = versions;
        SelectedVersion = versions.FirstOrDefault();
    }

    public IReadOnlyList<LoaderVersionItem> Versions { get; }
    [ObservableProperty] public partial LoaderVersionItem? SelectedVersion { get; set; }
    public bool CanConfirm => SelectedVersion is not null;

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    partial void OnSelectedVersionChanged(LoaderVersionItem? value)
    {
        OnPropertyChanged(nameof(CanConfirm));
    }

    public void Confirm()
    {
        RequestClose?.Invoke(this, SelectedVersion);
    }

    public void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}