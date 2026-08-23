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
        Apply(Data.ConfigEntry.ResourceDownloadSource);
    }

    public static void Apply(ResourceDownloadSourceMode mode)
    {
        SourceSelector.Configure((SourceSelectionMode)(int)mode);
    }
}
