using Iridium.Enums.Resources;
using Iridium.Models.Resources;
using Iridium.Providers.CurseForge;
using Iridium.Providers.Modrinth;
using Iridium.Services.Resources;
using MinecraftLaunch.Base.Enums;
using Portal.Core.Services;

namespace Portal.Core.Minecraft.Services;

/// <summary>Portal 共享的 Iridium 资源客户端与搜索服务（Modrinth / CurseForge）。</summary>
public static class IridiumResourceClients
{
    private static readonly object Sync = new();
    private static string? _curseForgeKey;
    private static CurseForgeClient? _curseForge;
    private static ResourceSearchService? _search;

    public static ModrinthClient Modrinth { get; } = new(BuildOptions());

    /// <summary>CurseForge 客户端。API Key 变化（如运行时注入环境变量）时自动重建。</summary>
    public static CurseForgeClient CurseForge
    {
        get
        {
            EnsureCurseForge();
            return _curseForge!;
        }
    }

    public static ResourceSearchService Search
    {
        get
        {
            lock (Sync)
            {
                EnsureCurseForgeCore();
                return _search ??= new ResourceSearchService(Modrinth, _curseForge!);
            }
        }
    }

    private static void EnsureCurseForge()
    {
        lock (Sync)
        {
            EnsureCurseForgeCore();
        }
    }

    private static void EnsureCurseForgeCore()
    {
        var key = CredentialsService.CurseForgeApiKey;
        if (_curseForge is not null && string.Equals(_curseForgeKey, key, StringComparison.Ordinal))
            return;
        _curseForgeKey = key;
        _curseForge = new CurseForgeClient(BuildOptions());
        _search = null;
    }

    private static ResourceApiOptions BuildOptions()
    {
        return new ResourceApiOptions
        {
            CurseForgeApiKey = CredentialsService.CurseForgeApiKey,
            UserAgent = $"Portal/{MinecraftCoreInitializer.AppVersion}"
        };
    }
}

/// <summary>Portal 枚举与 Iridium 资源枚举之间的转换。</summary>
public static class IridiumResourceMapping
{
    public static ResourceLoaderType ToResourceLoader(this ModLoaderType loader)
    {
        return loader switch
        {
            ModLoaderType.NeoForge => ResourceLoaderType.NeoForge,
            ModLoaderType.Forge => ResourceLoaderType.Forge,
            ModLoaderType.Fabric => ResourceLoaderType.Fabric,
            ModLoaderType.Quilt => ResourceLoaderType.Quilt,
            _ => ResourceLoaderType.Any
        };
    }

    public static ResourceLoaderType ParseResourceLoader(string loader)
    {
        return loader.Trim().ToLowerInvariant() switch
        {
            "neoforge" => ResourceLoaderType.NeoForge,
            "forge" => ResourceLoaderType.Forge,
            "fabric" => ResourceLoaderType.Fabric,
            "quilt" => ResourceLoaderType.Quilt,
            "paper" => ResourceLoaderType.Paper,
            "purpur" => ResourceLoaderType.Purpur,
            "spigot" => ResourceLoaderType.Spigot,
            "bukkit" => ResourceLoaderType.Bukkit,
            "velocity" => ResourceLoaderType.Velocity,
            "waterfall" => ResourceLoaderType.Waterfall,
            "bungeecord" => ResourceLoaderType.BungeeCord,
            "liteloader" => ResourceLoaderType.LiteLoader,
            "optifine" => ResourceLoaderType.OptiFine,
            "canvas" => ResourceLoaderType.Canvas,
            "iris" => ResourceLoaderType.Iris,
            _ => ResourceLoaderType.Any
        };
    }

    public static ResourceType ParseResourceType(string projectType)
    {
        return projectType.Trim().ToLowerInvariant() switch
        {
            "modpack" => ResourceType.Modpack,
            "resourcepack" => ResourceType.ResourcePack,
            "shader" => ResourceType.Shader,
            "datapack" => ResourceType.DataPack,
            "world" => ResourceType.World,
            "plugin" => ResourceType.Plugin,
            _ => ResourceType.Mod
        };
    }
}
