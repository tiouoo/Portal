using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Models;

public static class ResourceCompatibility
{
    /// <summary>实例已安装的加载器名称（小写，如 "fabric"），原版返回空集。</summary>
    public static IReadOnlyList<string> GetInstalledLoaders(MinecraftInstance instance)
    {
        if (instance.Type != MinecraftInstanceType.Java ||
            instance.MinecraftEntry is not ModifiedMinecraftEntry entry)
            return [];

        return entry.ModLoaders.Select(loader => LoaderName(loader.Type))
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>该版本文件是否兼容当前实例（模组按游戏版本+加载器，资源包/光影包仅按游戏版本）。</summary>
    public static bool IsCompatible(ModVersionFileItem file, MinecraftInstance instance, ResourceKind kind)
    {
        if (!file.MinecraftVersions.Contains(instance.VersionId, StringComparer.OrdinalIgnoreCase))
            return false;
        if (kind != ResourceKind.Mod)
            return true;

        var fileLoaders = file.GroupKeys.Select(key => key.Loader)
            .Where(loader => loader != "通用")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fileLoaders.Count == 0)
            return true;

        return GetInstalledLoaders(instance).Any(loader => fileLoaders.Contains(loader));
    }

    private static string? LoaderName(ModLoaderType loader)
    {
        return loader switch
        {
            ModLoaderType.NeoForge => "neoforge",
            ModLoaderType.Forge => "forge",
            ModLoaderType.Fabric => "fabric",
            ModLoaderType.Quilt => "quilt",
            _ => null
        };
    }
}
