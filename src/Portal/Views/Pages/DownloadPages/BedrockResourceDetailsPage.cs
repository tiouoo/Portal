using Avalonia.Controls;
using Portal.Core.Minecraft.Models;

namespace Portal.Views.Pages.DownloadPages;

public sealed class BedrockResourceDetailsPage : JavaResourceDetailsPage
{
    public BedrockResourceDetailsPage() : this(new JavaResourceDetailsTarget(
        BedrockResourceDefinitions.BehaviorPack, ModDetailsSource.CurseForge, string.Empty))
    {
    }

    public BedrockResourceDetailsPage(JavaResourceDetailsTarget target) : base(target)
    {
    }

    protected override JavaResourceDetailsViewModel CreateViewModel(JavaResourceDetailsTarget target)
    {
        return new BedrockResourceDetailsPageViewModel(target);
    }

    protected override Task VersionFileClickedAsync(TopLevel topLevel, JavaResourceFileItem file)
    {
        return BedrockResourceDownload.DownloadAsync(topLevel, ViewModel.Target.Definition, file);
    }
}
