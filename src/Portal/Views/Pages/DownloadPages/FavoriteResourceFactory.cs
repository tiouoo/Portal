using Portal.Core.Minecraft.Models;
using Portal.Core.Services;
using Portal.Services;

namespace Portal.Views.Pages.DownloadPages;

public static class FavoriteResourceFactory
{
    public static FavoriteResource From(ModSearchResultItem item) => new()
    {
        Name = item.FriendlyName, Summary = item.Summary, IconUrl = item.IconUrl,
        Edition = FavoriteEdition.Java, Kind = JavaResourceKind.Mod, Source = item.Target.Source, ProjectId = item.Target.ProjectId
    };

    public static FavoriteResource From(JavaResourceSearchResultItem item, FavoriteEdition edition) => new()
    {
        Name = item.Name, Summary = item.Summary, IconUrl = item.IconUrl,
        Edition = edition, Kind = item.Target.Definition.Kind, Source = item.Target.Source, ProjectId = item.Target.ProjectId
    };
}
