using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;

namespace Portal.Views.Pages.DownloadPages;

public static class FavoriteResourceFactory
{
    public static FavoriteResource From(ModSearchResultItem item)
    {
        return new FavoriteResource
        {
            Name = item.FriendlyName, Summary = item.Summary, IconUrl = item.IconUrl,
            Edition = FavoriteEdition.Java, Kind = ResourceKind.Mod, Source = item.Target.Source,
            ProjectId = item.Target.ProjectId, Tags = [.. item.Tags]
        };
    }

    public static FavoriteResource From(JavaResourceSearchResultItem item, FavoriteEdition edition)
    {
        return new FavoriteResource
        {
            Name = item.Name, Summary = item.Summary, IconUrl = item.IconUrl,
            Edition = edition, Kind = item.Target.Definition.Kind, Source = item.Target.Source,
            ProjectId = item.Target.ProjectId, Tags = [.. item.Tags]
        };
    }

    public static FavoriteResource From(LeviLaminaSearchResultItem item)
    {
        return new FavoriteResource
        {
            Name = item.Name, Summary = item.Summary, IconUrl = item.AvatarUrl,
            Edition = FavoriteEdition.Bedrock, Kind = ResourceKind.LeviLaminaMod,
            Source = ModDetailsSource.LeviLamina, ProjectId = item.Key, ProjectUrl = item.ProjectUrl,
            LatestVersion = item.LatestVersion, Tags = [.. item.Tags]
        };
    }
}
