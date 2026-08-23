using Iridium.Download;
using Iridium.Enums;
using Iridium.Helpers.Resources;
using Iridium.Interfaces.Resources;
using Portal.Core.Classes.Config;
using Portal.Core.Const;

namespace Portal.Core.Minecraft.Services;

public static class ResourceSourceService
{
    public static IResourceMirror ActiveResourceMirror { get; } = new TianpaoResourceMirror();

    public static void Initialize()
    {
        SourceSelector.ResourceMirror = ActiveResourceMirror;
        Apply(Data.ConfigEntry.ModrinthResourceDownloadSource, Data.ConfigEntry.CurseForgeResourceDownloadSource);
    }

    public static void Apply(ResourceDownloadSourceMode mode)
    {
        SourceSelector.Configure((SourceSelectionMode)(int)mode);
    }

    public static void Apply(ResourceDownloadSourceMode modrinth, ResourceDownloadSourceMode curseForge)
    {
        SourceSelector.ResourceMirror = ActiveResourceMirror;
        SourceSelector.ConfigureResourceMirror(Iridium.Enums.Resources.ResourceSource.Modrinth,
            (SourceSelectionMode)(int)modrinth);
        SourceSelector.ConfigureResourceMirror(Iridium.Enums.Resources.ResourceSource.CurseForge,
            (SourceSelectionMode)(int)curseForge);
    }
}
