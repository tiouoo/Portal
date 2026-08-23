using Iridium.Enums;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;

namespace Portal.Core.Minecraft.Models;

public static class ResourceCompatibility
{
    public static IReadOnlyList<string> GetInstalledLoaders(MinecraftInstance instance)
    {
        if (instance.Type != MinecraftInstanceType.Java ||
            instance.MinecraftEntry is not { } entry)
            return [];

        return entry.Loaders.Select(loader => LoaderName(loader.Type))
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsCompatible(ModVersionFileItem file, MinecraftInstance instance, ResourceKind kind)
    {
        if (!file.MinecraftVersions.Contains(instance.VersionId, StringComparer.OrdinalIgnoreCase))
            return false;
        if (kind != ResourceKind.Mod)
            return true;

        var fileLoaders = file.GroupKeys.Select(key => key.Loader)
            .Where(loader => loader != LinguaSentinels.UniversalLoader)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fileLoaders.Count == 0)
            return true;

        return GetInstalledLoaders(instance).Any(loader => fileLoaders.Contains(loader));
    }

    private static string? LoaderName(LoaderType loader)
    {
        return loader switch
        {
            LoaderType.NeoForge => "neoforge",
            LoaderType.Forge => "forge",
            LoaderType.Fabric => "fabric",
            LoaderType.Quilt => "quilt",
            _ => null
        };
    }
}
