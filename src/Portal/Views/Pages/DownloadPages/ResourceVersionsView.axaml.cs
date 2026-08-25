using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Core.Minecraft.Models;

namespace Portal.Views.Pages.DownloadPages;

public partial class ResourceVersionsView : UserControl
{
    public ResourceVersionsView()
    {
        InitializeComponent();
    }

    private async void VersionFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ResourceVersionFileItem file } ||
            DataContext is not ResourceDetailsViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        switch (viewModel.Target.Definition.Kind)
        {
            case ResourceKind.Mod:
                await ResourceDownload.ShowModInstallDialogAsync(topLevel, [file]);
                break;
            case ResourceKind.Modpack:
                await ModpackInstallation.HandleVersionFileClickAsync(viewModel, topLevel, file);
                break;
            case ResourceKind.BedrockBehaviorPack or ResourceKind.BedrockResourcePack
                or ResourceKind.BedrockWorld or ResourceKind.BedrockWorldTemplate:
                await BedrockResourceDownload.DownloadAsync(topLevel, viewModel.Target.Definition, file);
                break;
            default:
                await ResourceDownload.ShowInstallDialogAsync(topLevel, viewModel.Target.Definition, file);
                break;
        }
    }
}
