using Portal.Core.Minecraft.Models;

namespace Portal.Views.Pages.DownloadPages;

public sealed class DataPackDetailsPage : JavaResourceDetailsPage
{
    public DataPackDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.DataPack,
        ModDetailsSource.Modrinth, string.Empty))
    {
    }

    public DataPackDetailsPage(JavaResourceDetailsTarget target) : base(target)
    {
    }

    protected override JavaResourceDetailsViewModel CreateViewModel(JavaResourceDetailsTarget target)
    {
        return new DataPackDetailsPageViewModel(target);
    }
}
