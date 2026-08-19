using Portal.Core.Minecraft.Models;

namespace Portal.Views.Pages.DownloadPages;

public sealed class ResourcePackDetailsPage : JavaResourceDetailsPage
{
    public ResourcePackDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.ResourcePack,
        ModDetailsSource.Modrinth, string.Empty))
    {
    }

    public ResourcePackDetailsPage(JavaResourceDetailsTarget target) : base(target)
    {
    }

    protected override JavaResourceDetailsViewModel CreateViewModel(JavaResourceDetailsTarget target)
    {
        return new ResourcePackDetailsPageViewModel(target);
    }
}
