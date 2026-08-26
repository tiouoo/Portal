using System.Text.RegularExpressions;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Iridium.Enums;
using Iridium.Models.Resources;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using Portal.Core.App.Helpers;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Core.Services;
using Portal.Localization;

namespace Portal.Views.Pages.DownloadPages;

internal static class DownloadSearchPersistence
{
    public static DownloadSearchSource ToCoreSource(SearchSource source) => source switch
    {
        SearchSource.Modrinth => DownloadSearchSource.Modrinth,
        SearchSource.All => DownloadSearchSource.All,
        _ => DownloadSearchSource.CurseForge
    };

    public static SearchSource ToUiSource(DownloadSearchSource source) => source switch
    {
        DownloadSearchSource.Modrinth => SearchSource.Modrinth,
        DownloadSearchSource.All => SearchSource.All,
        _ => SearchSource.CurseForge
    };

    public static ResourceSource ToResourceSource(SearchSource source)
    {
        if (source == SearchSource.All && string.IsNullOrWhiteSpace(CredentialsService.CurseForgeApiKey))
            return ResourceSource.Modrinth;
        return source switch
        {
            SearchSource.Modrinth => ResourceSource.Modrinth,
            SearchSource.All => ResourceSource.All,
            _ => ResourceSource.CurseForge
        };
    }

    public static DownloadSearchSort ToCoreSort(SearchSort sort) => sort switch
    {
        SearchSort.Popularity => DownloadSearchSort.Popularity,
        SearchSort.Updated => DownloadSearchSort.Updated,
        SearchSort.Newest => DownloadSearchSort.Newest,
        _ => DownloadSearchSort.Relevance
    };

    public static SearchSort ToUiSort(DownloadSearchSort sort) => sort switch
    {
        DownloadSearchSort.Popularity => SearchSort.Popularity,
        DownloadSearchSort.Updated => SearchSort.Updated,
        DownloadSearchSort.Newest => SearchSort.Newest,
        _ => SearchSort.Relevance
    };

    public static string SourceAbbreviation(ResourceSource source)
    {
        return source switch
        {
            ResourceSource.Modrinth => "Modrinth",
            ResourceSource.CurseForge => "CurseForge",
            _ => string.Empty
        };
    }
}

internal static class ResourceSearchPresentation
{
    private static readonly IReadOnlyDictionary<string, string> ChineseTags = new Dictionary<string, string>
    {
        ["adventure"] = "冒险", ["adventure-rpg"] = "冒险与角色扮演",
        ["adventure-and-rpg"] = "冒险与角色扮演", ["animated"] = "动态效果",
        ["atmosphere"] = "氛围", ["audio"] = "音频", ["automation"] = "自动化",
        ["biomes"] = "生物群系", ["blocks"] = "方块", ["bloom"] = "泛光", ["cartoon"] = "卡通",
        ["challenging"] = "高难度", ["hardcore"] = "极限", ["combat"] = "战斗",
        ["combat-pvp"] = "战斗与 PvP", ["armor-weapons-tools"] = "盔甲、武器与工具",
        ["colored-lighting"] = "彩色光照", ["core-shaders"] = "核心着色器", ["creative"] = "创造",
        ["cursed"] = "诅咒", ["datapack"] = "数据包", ["decoration"] = "装饰",
        ["dimensions"] = "维度", ["display"] = "信息显示", ["energy"] = "能源",
        ["entities"] = "实体", ["equipment"] = "装备", ["exploration"] = "探索",
        ["fantasy"] = "奇幻", ["farming"] = "农业", ["fonts"] = "字体", ["food"] = "食物与烹饪",
        ["game-mechanics"] = "游戏机制", ["mechanics"] = "游戏机制", ["gui"] = "界面",
        ["map-information"] = "地图与信息", ["information"] = "信息",
        ["kitchen-sink"] = "水槽包", ["library"] = "支持库", ["library-api"] = "API 与库",
        ["lightweight"] = "轻量", ["locale"] = "语言", ["magic"] = "魔法", ["medieval"] = "中世纪",
        ["minigame"] = "小游戏", ["mini-game"] = "小游戏", ["mobs"] = "生物",
        ["world-mobs"] = "生物", ["models"] = "模型",
        ["modern"] = "现代", ["modded"] = "模组支持", ["mod-support"] = "模组支持",
        ["multiplayer"] = "多人游戏", ["optimization"] = "优化", ["performance"] = "性能优化",
        ["path-tracing"] = "路径追踪", ["pbr"] = "PBR", ["potato"] = "低配",
        ["quests"] = "任务", ["realistic"] = "写实", ["redstone"] = "红石", ["reflections"] = "反射",
        ["semi-realistic"] = "半写实", ["simplistic"] = "简约", ["social"] = "社交与服务器",
        ["chat-related"] = "聊天与社交",
        ["storage"] = "仓储", ["structures"] = "结构", ["technology"] = "科技",
        ["themed"] = "主题", ["transportation"] = "交通运输", ["tweaks"] = "调整",
        ["utility"] = "实用", ["utility-qol"] = "实用与生活质量", ["vanilla-like"] = "原版风格",
        ["worldgen"] = "世界生成", ["world-gen"] = "世界生成", ["world-biomes"] = "生物群系",
        ["ores"] = "矿石与资源", ["mc-food"] = "食物与烹饪",
        ["technology-player-transport"] = "交通运输", ["cosmetic"] = "装饰",
        ["pipes-logistics"] = "管道与物流", ["steampunk"] = "蒸汽朋克", ["server"] = "服务端",
        ["mod-related"] = "模组相关", ["parkour"] = "跑酷", ["puzzle"] = "解谜",
        ["survival"] = "生存", ["mod-world"] = "模组地图", ["skyblock"] = "空岛",
        ["vanilla-plus"] = "原版增强", ["small-light"] = "轻量", ["extra-large"] = "大型",
        ["low"] = "低性能需求", ["medium"] = "中等性能需求", ["high"] = "高性能需求",
        ["16x"] = "16x", ["32x"] = "32x", ["48x"] = "48x", ["64x"] = "64x",
        ["128x"] = "128x", ["256x"] = "256x", ["512x+"] = "512x 及以上", ["8x-"] = "8x 及以下",
        ["forge"] = "Forge", ["neoforge"] = "NeoForge", ["fabric"] = "Fabric", ["quilt"] = "Quilt",
        ["iris"] = "Iris", ["optifine"] = "OptiFine", ["vanilla"] = "原版"
    };

    public static IReadOnlyList<string> BuildTags(ResourceHit hit)
    {
        var source = DownloadSearchPersistence.SourceAbbreviation(hit.Source);
        return hit.Categories
            .Select(LocalizeCategory)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Where(tag => !hit.Loaders.Any(loader =>
                string.Equals(loader.ToString(), tag, StringComparison.OrdinalIgnoreCase)))
            .Prepend(source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    public static string LocalizeCategory(ResourceCategory category)
    {
        var fallback = category.DisplayName ?? category.Name;
        foreach (var value in new[] { category.ModrinthSlug, category.Name, category.DisplayName })
            if (!string.IsNullOrWhiteSpace(value) && TryLocalizeTag(value, out var localized)) return localized;
        return fallback;
    }

    public static string LocalizeTag(string tag) => TryLocalizeTag(tag, out var localized) ? localized : tag;

    private static bool TryLocalizeTag(string value, out string localized)
    {
        localized = value;
        if (!LocalizationService.CurrentCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return false;
        var key = Regex.Replace(value.Trim().ToLowerInvariant().Replace('&', ' '), @"[^a-z0-9+]+", "-").Trim('-');
        if (ChineseTags.TryGetValue(key, out localized!)) return true;
        foreach (var prefix in new[] { "mc-", "world-", "armor-weapons-tools-" })
            if (key.StartsWith(prefix, StringComparison.Ordinal) && ChineseTags.TryGetValue(key[prefix.Length..], out localized!))
                return true;
        return false;
    }

    public static string FormatDownloads(long downloads)
    {
        var culture = LocalizationService.CurrentCulture;
        if (!culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return downloads switch
            {
                >= 1_000_000_000 => $"{downloads / 1_000_000_000d:0.#}B",
                >= 1_000_000 => $"{downloads / 1_000_000d:0.#}M",
                >= 1_000 => $"{downloads / 1_000d:0.#}K",
                _ => downloads.ToString("N0", culture)
            };

        return downloads switch
        {
            >= 100_000_000 => $"{downloads / 100_000_000d:0.#}亿",
            >= 10_000 => $"{downloads / 10_000d:0.#}万",
            >= 1_000 => $"{downloads / 1_000d:0.#}千",
            _ => downloads.ToString("N0", CultureInfo.CurrentCulture)
        };
    }

    public static string FormatMetadata(DateTime timestamp, long downloads)
    {
        var format = CommonLanguageManager.Instance.mod_downloadCount.CurrentValue()
            .Replace("{1:N0}", "{1}", StringComparison.Ordinal);
        return string.Format(format, RelativeTime.Format(timestamp), FormatDownloads(downloads));
    }
}

public sealed partial class ResourceFilterOption(string displayName, string id, Action changed) : ObservableObject
{
    public string DisplayName { get; } = displayName;
    public string Id { get; } = id;

    [ObservableProperty] public partial bool? IsSelected { get; set; } = false;

    partial void OnIsSelectedChanged(bool? value) => changed();
}

internal static class ResourceFilterCatalog
{
    public static IReadOnlyList<(string Name, string Id)> Get(ResourceKind kind) =>
        GetRaw(kind).Select(item => (Localize(item.Name), item.Id)).ToArray();

    private static IReadOnlyList<(string Name, string Id)> GetRaw(ResourceKind kind) => kind switch
    {
        ResourceKind.Mod =>
        [
            ("世界生成", "406/worldgen"), ("科技", "412/technology"), ("食物与烹饪", "436/food"),
            ("交通运输", "414/transportation"), ("仓储", "420/storage"), ("魔法", "419/magic"),
            ("冒险", "422/adventure"), ("装饰", "424/decoration"), ("生物", "411/mobs"),
            ("装备", "434/equipment"), ("实用", "5191/utility"), ("优化", "6814/optimization"),
            ("社交与服务器", "435/social"), ("API 与库", "421/library"), ("游戏机制", "/game-mechanics"),
            ("生物群系", "407/"), ("维度", "410/"), ("矿石与资源", "408/"), ("结构", "409/"),
            ("管道与物流", "415/"), ("自动化", "4843/"), ("能源", "417/"), ("红石", "4558/"),
            ("农业", "416/"), ("创造模式", "9026/"), ("信息显示", "423/")
        ],
        ResourceKind.Modpack =>
        [
            ("多人游戏", "4484/"), ("优化", "/optimization"), ("高难度", "4479/challenging"),
            ("战斗", "4483/combat"), ("任务", "4478/quests"), ("科技", "4472/technology"),
            ("魔法", "4473/magic"), ("冒险", "4475/adventure"), ("水槽包", "/kitchen-sink"),
            ("探索", "4476/"), ("小游戏", "4477/"), ("空岛", "4736/"), ("原版增强", "5128/"),
            ("轻量", "4481/lightweight"), ("大型", "4482/")
        ],
        ResourceKind.ResourcePack =>
        [
            ("原版风格", "403/vanilla-like"), ("写实", "400/realistic"), ("现代", "401/"),
            ("中世纪", "402/"), ("蒸汽朋克", "399/"), ("主题", "/themed"), ("简约", "/simplistic"),
            ("装饰", "/decoration"), ("战斗", "/combat"), ("实用", "/utility"), ("调整", "/tweaks"),
            ("实体", "/entities"), ("音频", "/audio"), ("字体", "5244/fonts"), ("模型", "/models"),
            ("语言", "/locale"), ("界面", "/gui"), ("动画", "404/"), ("模组支持", "4465/modded"),
            ("16x", "393/16x"), ("32x", "394/32x"), ("64x", "395/64x"),
            ("128x", "396/128x"), ("256x", "397/256x"), ("512x+", "398/512x+")
        ],
        ResourceKind.ShaderPack =>
        [
            ("原版风格", "6555/vanilla-like"), ("幻想", "6554/fantasy"), ("写实", "6553/realistic"),
            ("半写实", "/semi-realistic"), ("卡通", "/cartoon"), ("彩色光照", "/colored-lighting"),
            ("路径追踪", "/path-tracing"), ("PBR", "/pbr"), ("反射", "/reflections"),
            ("低配", "/potato"), ("低性能需求", "/low"), ("中等性能需求", "/medium"),
            ("高性能需求", "/high")
        ],
        ResourceKind.DataPack =>
        [
            ("世界生成", "/worldgen"), ("科技", "6951/technology"), ("游戏机制", "/game-mechanics"),
            ("交通运输", "/transportation"), ("仓储", "/storage"), ("魔法", "6952/magic"),
            ("冒险", "6948/adventure"), ("幻想", "6949/"), ("装饰", "/decoration"),
            ("生物", "/mobs"), ("实用", "6953/utility"), ("装备", "/equipment"),
            ("优化", "/optimization"), ("社交与服务器", "/social"), ("库", "6950/library"),
            ("模组相关", "6946/")
        ],
        ResourceKind.Save =>
        [
            ("冒险", "248/"), ("创造", "249/"), ("小游戏", "250/"), ("跑酷", "251/"),
            ("解谜", "252/"), ("生存", "253/"), ("模组地图", "4464/")
        ],
        _ => []
    };

    private static string Localize(string name)
    {
        if (LocalizationService.CurrentCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return name;
        return name switch
        {
            "世界生成" => "World Generation", "科技" => "Technology", "食物与烹饪" => "Food & Cooking",
            "交通运输" => "Transportation", "仓储" => "Storage", "魔法" => "Magic", "冒险" => "Adventure",
            "装饰" => "Decoration", "生物" => "Mobs", "装备" => "Equipment", "实用" => "Utility",
            "优化" => "Optimization", "社交与服务器" => "Social & Server", "API 与库" => "API & Libraries",
            "游戏机制" => "Game Mechanics", "生物群系" => "Biomes", "维度" => "Dimensions",
            "矿石与资源" => "Ores & Resources", "结构" => "Structures", "管道与物流" => "Pipes & Logistics",
            "自动化" => "Automation", "能源" => "Energy", "红石" => "Redstone", "农业" => "Farming",
            "创造模式" => "Creative Mode", "信息显示" => "Information Display", "多人游戏" => "Multiplayer",
            "高难度" => "Challenging", "战斗" => "Combat", "任务" => "Quests", "水槽包" => "Kitchen Sink",
            "探索" => "Exploration", "小游戏" => "Mini Games", "空岛" => "Skyblock", "原版增强" => "Vanilla+",
            "轻量" => "Lightweight", "大型" => "Extra Large", "原版风格" => "Vanilla-like",
            "写实" => "Realistic", "现代" => "Modern", "中世纪" => "Medieval", "蒸汽朋克" => "Steampunk",
            "主题" => "Themed", "简约" => "Simplistic", "调整" => "Tweaks", "实体" => "Entities",
            "音频" => "Audio", "字体" => "Fonts", "模型" => "Models", "语言" => "Locale", "界面" => "GUI",
            "动画" => "Animated", "模组支持" => "Mod Support", "幻想" => "Fantasy", "半写实" => "Semi-realistic",
            "卡通" => "Cartoon", "彩色光照" => "Colored Lighting", "路径追踪" => "Path Tracing",
            "反射" => "Reflections", "低配" => "Potato", "低性能需求" => "Low", "中等性能需求" => "Medium",
            "高性能需求" => "High", "库" => "Library", "模组相关" => "Mod-related", "创造" => "Creative",
            "跑酷" => "Parkour", "解谜" => "Puzzle", "生存" => "Survival", "模组地图" => "Modded World",
            _ => name
        };
    }

    public static IReadOnlyList<ResourceCategory> Parse(string categories, ResourceType type)
    {
        if (string.IsNullOrWhiteSpace(categories)) return [];
        return categories.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(category =>
        {
            var parts = category.Split('/', 2);
            return new ResourceCategory
            {
                Type = type,
                CurseForgeId = int.TryParse(parts[0], out var id) && id > 0 ? id : null,
                ModrinthSlug = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : null
            };
        }).ToArray();
    }

    public static ResourceSource ResolveSource(SearchSource selectedSource, IReadOnlyList<ResourceCategory> includedTags,
        IReadOnlyList<ResourceCategory> excludedTags, ResourceEnvironment environment)
    {
        var source = DownloadSearchPersistence.ToResourceSource(selectedSource);
        if (excludedTags.Count > 0 || environment != ResourceEnvironment.Any)
            source &= ResourceSource.Modrinth;
        if (includedTags.Concat(excludedTags).Any(tag => string.IsNullOrWhiteSpace(tag.ModrinthSlug)))
            source &= ~ResourceSource.Modrinth;
        if (includedTags.Any(tag => !tag.CurseForgeId.HasValue))
            source &= ~ResourceSource.CurseForge;
        return source;
    }
}

public readonly record struct MinecraftVersionSortKey(int Major, int Minor, int Patch, int Stage)
    : IComparable<MinecraftVersionSortKey>
{
    public int CompareTo(MinecraftVersionSortKey other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : Stage.CompareTo(other.Stage);
    }
}

internal static class MinecraftVersionParsing
{
    public static MinecraftVersionSortKey Parse(string value)
    {
        var match = Regex.Match(value, @"^(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?<suffix>.*)$");
        if (!match.Success) return new MinecraftVersionSortKey(-1, -1, -1, -1);
        var suffix = match.Groups["suffix"].Value;
        var stage = string.IsNullOrEmpty(suffix) ? 3 :
            suffix.Contains("rc", StringComparison.OrdinalIgnoreCase) ? 2 :
            suffix.Contains("pre", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return new MinecraftVersionSortKey(int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0, stage);
    }

    public static ResourceSort ToResourceSort(SearchSort sort)
    {
        return sort switch
        {
            SearchSort.Popularity => ResourceSort.Downloads,
            SearchSort.Updated => ResourceSort.Updated,
            SearchSort.Newest => ResourceSort.Newest,
            _ => ResourceSort.Relevance
        };
    }
}

internal static class MinecraftVersionLoader
{
    private static readonly SemaphoreSlim VersionLoadLock = new(1, 1);
    private static Task<IReadOnlyList<VersionManifestEntry>>? _versionLoadTask;

    public static async Task<IReadOnlyList<string>> LoadReleaseVersionsAsync(CancellationToken cancellationToken)
    {
        await VersionLoadLock.WaitAsync(cancellationToken);
        try
        {
            var entries = Data.UiProperty.MinecraftVersionManifestEntries;
            if (_versionLoadTask is { IsCompleted: true, IsCompletedSuccessfully: false })
                _versionLoadTask = null;
            _versionLoadTask ??= entries.Count == 0
                ? LoadReleaseManifestAsync()
                : Task.FromResult<IReadOnlyList<VersionManifestEntry>>(entries);
            var loadedEntries = await _versionLoadTask.WaitAsync(cancellationToken);
            if (entries.Count == 0) entries.AddRange(loadedEntries);
            return entries.Where(x => x.Type == "release").Select(x => x.Id).Distinct()
                .OrderByDescending(MinecraftVersionParsing.Parse)
                .ThenByDescending(x => x, StringComparer.Ordinal)
                .ToList();
        }
        finally
        {
            VersionLoadLock.Release();
        }
    }

    private static async Task<IReadOnlyList<VersionManifestEntry>> LoadReleaseManifestAsync()
    {
        var entries = (await VanillaInstaller.EnumerableMinecraftAsync()).ToList();
        UnlistedVersions.MergeInto(entries);
        return entries;
    }
}

internal static class CancellationTokens
{
    public static void CancelInBackground(CancellationTokenSource cancellation)
    {
        _ = CancelAndDisposeAsync(cancellation);
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
