using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Bedrock.Standard.Interface;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

public partial class BedrockInstallDialog : UserControl
{
    public BedrockInstallDialog()
    {
        InitializeComponent();
    }

    private void Install_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as BedrockInstallDialogViewModel)?.Install();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as BedrockInstallDialogViewModel)?.Cancel();
    }
}

public sealed record BedrockInstallDialogResult(MinecraftFolderEntry Folder);

public partial class BedrockInstallDialogViewModel : ObservableObject, IDialogContext
{
    private readonly BedrockInstallationViewModel _installation;
    private readonly BedrockGdkVersion _version;

    public BedrockInstallDialogViewModel(BedrockGdkVersion version, IReadOnlyList<MinecraftFolderEntry> folders,
        MinecraftFolderEntry selectedFolder, BedrockInstallationViewModel installation)
    {
        _version = version;
        _installation = installation;
        Folders = folders;
        SelectedFolder = selectedFolder;
    }

    public IReadOnlyList<MinecraftFolderEntry> Folders { get; }
    public string Details => _installation.GetInstallDetails(_version, SelectedFolder);
    public string DestinationPath => _installation.GetDestinationPath(_version, SelectedFolder);
    public bool CanInstall => SelectedFolder is not null;
    [ObservableProperty] public partial MinecraftFolderEntry SelectedFolder { get; set; }

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    partial void OnSelectedFolderChanged(MinecraftFolderEntry value)
    {
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(DestinationPath));
        OnPropertyChanged(nameof(CanInstall));
    }

    public void Install()
    {
        RequestClose?.Invoke(this, new BedrockInstallDialogResult(SelectedFolder));
    }

    public void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}