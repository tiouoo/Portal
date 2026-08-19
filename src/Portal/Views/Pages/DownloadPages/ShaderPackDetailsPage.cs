using Portal.Core.Minecraft.Models;

namespace Portal.Views.Pages.DownloadPages;

public sealed class ShaderPackDetailsPage : JavaResourceDetailsPage
{
    public ShaderPackDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.ShaderPack,
        ModDetailsSource.Modrinth, string.Empty))
    {
    }

    public ShaderPackDetailsPage(JavaResourceDetailsTarget target) : base(target)
    {
    }

    protected override JavaResourceDetailsViewModel CreateViewModel(JavaResourceDetailsTarget target)
    {
        return new ShaderPackDetailsPageViewModel(target);
    }
}
