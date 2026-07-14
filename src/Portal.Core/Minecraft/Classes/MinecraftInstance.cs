using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Models.Game;
using Newtonsoft.Json;
using System.IO;
using System.Reflection;
using Avalonia.Media.Imaging;
using MinecraftLaunch.Base.Enums;

namespace Portal.Core.Minecraft.Classes;

public class MinecraftInstance : ObservableObject
{
    public MinecraftEntry MinecraftEntry { get; init; }
    public string FolderName { get; init; }
    public string MinecraftPath => Path.GetDirectoryName(MinecraftEntry.ClientJarPath);
    public MinecraftInstanceConfig Config => field ??= GetInstanceConfig();

    public Bitmap Icon => field ??= GetInstanceIcon();
    
    public string LoaderDescription => MinecraftEntry.IsVanilla
        ? "原版"
        : string.Join(", ", (MinecraftEntry as ModifiedMinecraftEntry)?
            .ModLoaders.Select(x => x.Type.ToString()) ?? []);

    public string ShortDisplay => $"{LoaderDescription}·{MinecraftEntry.Version.VersionId}";

    public MinecraftInstance(MinecraftEntry e)
    {
        MinecraftEntry = e;
    }

    private MinecraftInstanceConfig GetInstanceConfig()
    {
        var configPath = Path.Combine(MinecraftPath, "Portal.config.json");
        if (File.Exists(configPath))
            return JsonConvert.DeserializeObject<MinecraftInstanceConfig>(File.ReadAllText(configPath));

        var config = new MinecraftInstanceConfig();
        File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
        return config;
    }

    public string GetSpecialFolder(MinecraftSpecialFolder folder)
    {
        var basePath = Path.Combine(MinecraftEntry.MinecraftFolderPath, "versions", MinecraftEntry.Id);
        var path = folder switch
        {
            MinecraftSpecialFolder.InstanceFolder => basePath,
            MinecraftSpecialFolder.ModsFolder => Path.Combine(basePath, "mods"),
            MinecraftSpecialFolder.ResourcePacksFolder => Path.Combine(basePath, "resourcepacks"),
            MinecraftSpecialFolder.SavesFolder => Path.Combine(basePath, "saves"),
            MinecraftSpecialFolder.ScreenshotsFolder => Path.Combine(basePath, "screenshots"),
            MinecraftSpecialFolder.ShaderPacksFolder => Path.Combine(basePath, "shaderpacks"),
            _ => basePath
        };

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    private Bitmap GetInstanceIcon()
    {
        var instanceFolder = GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder);
        var customIcon = Path.Combine(instanceFolder, "icon.png");
        if (File.Exists(customIcon))
        {
            using var s = File.OpenRead(customIcon);
            return Bitmap.DecodeToWidth(s, 48);
        }

        var pclIcon = Path.Combine(instanceFolder, "PCL", "Logo.png");
        if (File.Exists(pclIcon))
        {
            using var s = File.OpenRead(pclIcon);
            return Bitmap.DecodeToWidth(s, 48);
        }

        var iconName = GetEmbeddedIconName();
        return LoadBitmapFromAssembly(iconName);
    }

    private string GetEmbeddedIconName()
    {
        if (MinecraftEntry.IsVanilla)
        {
            return MinecraftEntry.Version.Type switch
            {
                MinecraftVersionType.Snapshot => "crafting_table_front.png",
                _ => "grass_block_side.png"
            };
        }

        if (MinecraftEntry is ModifiedMinecraftEntry e && e.ModLoaders != null)
        {
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.Forge)) return "ForgeIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.NeoForge)) return "NeoForgeIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.Fabric)) return "FabricIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.Quilt)) return "QuiltIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.OptiFine)) return "OptiFineIcon.png";
        }

        return "grass_block_side.png";
    }

    private static Bitmap LoadBitmapFromAssembly(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePath = $"Portal.Core.Assets.McIcons.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream == null)
        {
            var defaultPath = "Portal.Core.Assts.McIcons.grass_block_side.png";
            using var defaultStream = assembly.GetManifestResourceStream(defaultPath);
            return defaultStream != null ? Bitmap.DecodeToWidth(defaultStream, 48) : null;
        }

        return Bitmap.DecodeToWidth(stream, 48);
    }
}

public partial class MinecraftInstanceConfig : ObservableObject
{
    [ObservableProperty] public partial string Note { get; set; }
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    [ObservableProperty] public partial bool EnableIndependentInstance { get; set; } = true;
}