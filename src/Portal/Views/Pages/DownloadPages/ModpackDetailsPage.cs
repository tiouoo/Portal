using Avalonia.Controls;
using Portal.Core.Minecraft.Models;

namespace Portal.Views.Pages.DownloadPages;

public sealed class ModpackDetailsPage : JavaResourceDetailsPage
{
    public ModpackDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.Modpack,
        ModDetailsSource.Modrinth, string.Empty))
    {
    }

    public ModpackDetailsPage(JavaResourceDetailsTarget target) : base(target)
    {
    }

    protected override JavaResourceDetailsViewModel CreateViewModel(JavaResourceDetailsTarget target)
    {
        return new ModpackDetailsPageViewModel(target);
    }

    protected override Task VersionFileClickedAsync(TopLevel topLevel, JavaResourceFileItem file)
    {
        return ModpackInstallation.HandleVersionFileClickAsync(ViewModel, topLevel, file);
    }
}
