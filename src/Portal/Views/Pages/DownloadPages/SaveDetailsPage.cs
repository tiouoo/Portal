using Portal.Core.Minecraft.Models;

namespace Portal.Views.Pages.DownloadPages;

public sealed class SaveDetailsPage : JavaResourceDetailsPage
{
    public SaveDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.Save,
        ModDetailsSource.CurseForge, string.Empty))
    {
    }

    public SaveDetailsPage(JavaResourceDetailsTarget target) : base(target)
    {
    }

    protected override JavaResourceDetailsViewModel CreateViewModel(JavaResourceDetailsTarget target)
    {
        return new SaveDetailsPageViewModel(target);
    }
}
