using Iridium.Enums;
using Iridium.Models.Resources;
using Iridium.Resources;
using Iridium.Resources.CurseForge;
using Iridium.Resources.Modrinth;
using MinecraftLaunch.Base.Enums;
using Portal.Core.Services;

namespace Portal.Core.Minecraft.Services;

/// <summary>Portal 共享的 Iridium 资源客户端与聚合搜索（Modrinth / CurseForge）。</summary>
public static class IridiumResourceClients
{
    private static readonly object Sync = new();
    private static string? _curseForgeKey;
    private static CurseForgeClient? _curseForge;
    private static ResourceProvider? _provider;

    public static ModrinthClient Modrinth { get; } = new();

    /// <summary>CurseForge 客户端。API Key 变化（如运行时注入环境变量）时自动重建。</summary>
    public static CurseForgeClient CurseForge
    {
        get
        {
            EnsureCurseForge();
            return _curseForge ?? throw new InvalidOperationException("CurseForge 未配置 API Key，无法访问 CurseForge API。");
        }
    }

    /// <summary>Modrinth / CurseForge 聚合搜索。</summary>
    public static ResourceProvider Search
    {
        get
        {
            lock (Sync)
            {
                EnsureCurseForgeCore();
                return _provider ??= _curseForge is null
                    ? new ResourceProvider()
                    : new ResourceProvider(Modrinth, _curseForge);
            }
        }
    }

    /// <summary>为搜索结果批量获取中文翻译并写入 <see cref="ResourceHit.Translation"/>。</summary>
    public static async Task<IReadOnlyList<ResourceHit>> TranslateAsync(
        IReadOnlyList<ResourceHit> hits, CancellationToken cancellationToken = default)
    {
        var modrinthIds = hits.Where(hit => hit.Source == ResourceSource.Modrinth).Select(hit => hit.Id).ToArray();
        var curseForgeIds = hits.Where(hit => hit.Source == ResourceSource.CurseForge).Select(hit => hit.Id).ToArray();

        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        if (modrinthIds.Length > 0)
        {
            var result = await ProjectTranslationService.GetTranslationsAsync(
                ProjectTranslationSource.Modrinth, modrinthIds, cancellationToken);
            foreach (var pair in result)
                translations[$"{ResourceSource.Modrinth}:{pair.Key}"] = pair.Value;
        }

        if (curseForgeIds.Length > 0)
        {
            var result = await ProjectTranslationService.GetTranslationsAsync(
                ProjectTranslationSource.CurseForge, curseForgeIds, cancellationToken);
            foreach (var pair in result)
                translations[$"{ResourceSource.CurseForge}:{pair.Key}"] = pair.Value;
        }

        if (translations.Count == 0)
            return hits;

        return hits.Select(hit => translations.TryGetValue($"{hit.Source}:{hit.Id}", out var translated)
                ? hit with { Translation = translated }
                : hit)
            .ToArray();
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
        _curseForge = string.IsNullOrWhiteSpace(key) ? null : new CurseForgeClient(key);
        _provider = null;
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
