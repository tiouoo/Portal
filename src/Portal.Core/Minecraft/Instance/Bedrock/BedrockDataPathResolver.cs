using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Instance.Bedrock;

public static class BedrockDataPathResolver
{
    public static string GetDataRoot(BedrockInstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.EnableIndependentInstance)
            return config.EnableLauncherSharedData
                ? GetInstanceSharedDataRoot(config)
                : GetPortalIsolationRoot(config.InstancePath);

        return config.EnableLauncherSharedData ? GetWindowsDataRoot(config) : GetPortalDataRoot();
    }

    private static string GetPortalIsolationRoot(string instancePath) =>
        Path.Combine(instancePath, "config", "Portal", "isolation");

    private static string GetInstanceSharedDataRoot(BedrockInstanceConfig config) =>
        Path.Combine(config.InstancePath, GetBedrockFolderName(config));

    private static string GetWindowsDataRoot(BedrockInstanceConfig config) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), GetBedrockFolderName(config));

    private static string GetBedrockFolderName(BedrockInstanceConfig config) =>
        config.Type == BedrockInstanceReleaseType.Release ? "Minecraft Bedrock" : "Minecraft Bedrock Preview";

    public static string GetMojangDataRoot(BedrockInstanceConfig config, string userId = "Shared")
    {
        var root = GetDataRoot(config);
        return Path.Combine(root, "Users", userId, "games", "com.mojang");
    }

    public static IReadOnlyList<string> GetWorldUserIds(BedrockInstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var usersRoot = Path.Combine(GetDataRoot(config), "Users");
        if (!Directory.Exists(usersRoot))
            return ["Shared"];

        var userIds = Directory.EnumerateDirectories(usersRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => string.Equals(name, "Shared", StringComparison.OrdinalIgnoreCase))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!userIds.Contains("Shared", StringComparer.OrdinalIgnoreCase))
            userIds.Add("Shared");

        return userIds;
    }

    public static string GetWorldsFolder(BedrockInstanceConfig config, string userId) =>
        Path.Combine(GetMojangDataRoot(config, userId), "minecraftWorlds");

    public static void EnsurePortalDataDirectories()
    {
        var mojangRoot = Path.Combine(GetPortalDataRoot(), "Users", "Shared", "games", "com.mojang");
        foreach (var folder in new[] { "behavior_packs", "minecraftpe", "minecraftWorlds", "resource_packs", "Screenshots", "skin_packs" })
            Directory.CreateDirectory(Path.Combine(mojangRoot, folder));
    }

    private static string GetPortalDataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xyz.tiouo.Portal", "Bedrock");

    public static string GetFolder(BedrockInstanceConfig config, MinecraftSpecialFolder folder) => folder switch
    {
        MinecraftSpecialFolder.InstanceFolder => config.InstancePath,
        MinecraftSpecialFolder.SavesFolder => GetWorldsFolder(config, "Shared"),
        MinecraftSpecialFolder.ResourcePacksFolder => Path.Combine(GetMojangDataRoot(config), "resource_packs"),
        MinecraftSpecialFolder.BehaviorPacksFolder => Path.Combine(GetMojangDataRoot(config), "behavior_packs"),
        MinecraftSpecialFolder.SkinPacksFolder => Path.Combine(GetMojangDataRoot(config), "skin_packs"),
        MinecraftSpecialFolder.WorldTemplatesFolder => Path.Combine(GetMojangDataRoot(config), "world_templates"),
        MinecraftSpecialFolder.DevelopmentResourcePacksFolder => Path.Combine(GetMojangDataRoot(config), "development_resource_packs"),
        MinecraftSpecialFolder.DevelopmentBehaviorPacksFolder => Path.Combine(GetMojangDataRoot(config), "development_behavior_packs"),
        MinecraftSpecialFolder.ScreenshotsFolder => Path.Combine(GetMojangDataRoot(config), "Screenshots"),
        MinecraftSpecialFolder.ConfigFolder => Path.Combine(config.InstancePath, BedrockHelper.ConfigFolder),
        _ => config.InstancePath
    };
}
