using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Models.Game;
using Newtonsoft.Json;
using System.IO;
using System.Reflection;
using Avalonia.Media.Imaging;
using MinecraftLaunch.Base.Enums;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Minecraft.Instance;

namespace Portal.Core.Minecraft.Classes;

public class MinecraftInstance : ObservableObject
{
    public MinecraftInstanceType Type { get; init; }

    public MinecraftEntry? MinecraftEntry { get; init; }

    public BedrockInstanceConfig? BedrockConfig { get; init; }

    public string FolderName { get; init; }
    public string FolderPath { get; init; }

    public string InstanceFolderPath { get; init; }

    public DateTime LastPlayTime => Config?.LastPlayTime ?? DateTime.MinValue;

    [JsonIgnore]
    public string DisplayLastPlayTime
    {
        get
        {
            var time = LastPlayTime;
            if (time == DateTime.MinValue)
                return "从未游玩";

            var timeSpan = DateTime.Now - time;

            if (timeSpan.TotalMinutes < 1)
                return "刚刚";

            if (!(timeSpan.TotalDays <= 30)) return time.ToString("yyyy-MM-dd HH:mm");
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} 天前";

            return timeSpan.TotalHours >= 1 ? $"{(int)timeSpan.TotalHours} 小时前" : $"{(int)timeSpan.TotalMinutes} 分钟前";
        }
    }

    public string MinecraftPath
    {
        get
        {
            if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
                return Path.GetDirectoryName(MinecraftEntry.ClientJarPath);
            return InstanceFolderPath;
        }
    }

    public string InstanceName
    {
        get
        {
            string id;
            if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
                id = MinecraftEntry.Id;
            else if (Type == MinecraftInstanceType.Bedrock && BedrockConfig != null)
                id = BedrockConfig.Name;
            else
                return string.Empty;

            var note = Config?.Note?.Trim();
            if (!string.IsNullOrEmpty(note))
                return $"{note} ({id})";

            return id;
        }
    }

    public string VersionId
    {
        get
        {
            if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
                return MinecraftEntry.Version.VersionId;
            if (Type == MinecraftInstanceType.Bedrock && BedrockConfig != null)
                return BedrockConfig.Version;
            return string.Empty;
        }
    }

    public bool IsVanilla
    {
        get
        {
            if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
                return MinecraftEntry.IsVanilla;
            return false;
        }
    }

    public MinecraftInstanceConfig Config => field ??= GetInstanceConfig();

    public Bitmap Icon => field ??= GetInstanceIcon();

    public string LoaderDescription
    {
        get
        {
            if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
            {
                return MinecraftEntry.IsVanilla
                    ? "原版"
                    : string.Join(", ", (MinecraftEntry as ModifiedMinecraftEntry)?
                        .ModLoaders.Select(x => x.Type.ToString()) ?? []);
            }
            if (Type == MinecraftInstanceType.Bedrock)
            {
                return "基岩版";
            }
            return string.Empty;
        }
    }

    public string ShortDisplay => $"{LoaderDescription}·{VersionId}";

    public string FullInfo
    {
        get
        {
            var info = new List<string>();
            
            string id;
            if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
                id = MinecraftEntry.Id;
            else if (Type == MinecraftInstanceType.Bedrock && BedrockConfig != null)
                id = BedrockConfig.Name;
            else
                id = string.Empty;

            if (!string.IsNullOrEmpty(id))
                info.Add($"ID: {id}");

            var note = Config?.Note?.Trim();
            if (!string.IsNullOrEmpty(note))
                info.Add($"备注: {note}");

            if (!string.IsNullOrEmpty(FolderName))
                info.Add($"文件夹: {FolderName}");

            if (!string.IsNullOrEmpty(LoaderDescription))
                info.Add($"加载器: {LoaderDescription}");

            if (!string.IsNullOrEmpty(VersionId))
                info.Add($"版本: {VersionId}");

            if (!string.IsNullOrEmpty(VersionType))
                info.Add($"类型: {VersionType}");

            if (!string.IsNullOrEmpty(DisplayLastPlayTime))
                info.Add($"最近游玩: {DisplayLastPlayTime}");

            if (Config != null)
            {
                var playTime = Config.PlayTimeSeconds;
                if (playTime > 0)
                {
                    string timeStr;
                    if (playTime < 60)
                        timeStr = $"{playTime}秒";
                    else if (playTime < 3600)
                        timeStr = $"{playTime / 60.0:F1}分钟";
                    else
                        timeStr = $"{playTime / 3600.0:F1}小时";
                    info.Add($"游玩时长: {timeStr}");
                }

                if (Config.PlaySessions > 0)
                    info.Add($"会话次数: {Config.PlaySessions}次");
            }

            return string.Join("\n", info);
        }
    }

    public MinecraftInstance(MinecraftEntry e)
    {
        Type = MinecraftInstanceType.Java;
        MinecraftEntry = e;
        InstanceFolderPath = Path.GetDirectoryName(e.ClientJarPath);
    }

    public string Description
    {
        get
        {
            if (Type == MinecraftInstanceType.Bedrock && BedrockConfig != null)
                return BedrockConfig.Description ?? string.Empty;
            return Config?.Note ?? string.Empty;
        }
    }

    public string VersionType
    {
        get
        {
            if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
                return MinecraftEntry.Version.Type.ToString();
            if (Type == MinecraftInstanceType.Bedrock && BedrockConfig != null)
                return BedrockConfig.Type.ToString();
            return string.Empty;
        }
    }

    public MinecraftInstance(BedrockInstanceConfig bedrockConfig, string folderName, string folderPath)
    {
        Type = MinecraftInstanceType.Bedrock;
        BedrockConfig = bedrockConfig;
        FolderName = folderName;
        FolderPath = folderPath;
        InstanceFolderPath = bedrockConfig.InstancePath;
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

    public void SaveConfig()
    {
        var configPath = Path.Combine(MinecraftPath, "Portal.config.json");
        File.WriteAllText(configPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
    }

    /// <summary>
    /// 增加游玩时长（秒），立即保存配置文件
    /// 用于手动增加时长的场景（如导入历史数据）
    /// </summary>
    /// <param name="seconds">要增加的秒数</param>
    public void AddPlayTime(long seconds)
    {
        AddPlayTime(seconds, true);
    }

    /// <summary>
    /// 增加游玩时长（秒）
    /// </summary>
    /// <param name="seconds">要增加的秒数</param>
    /// <param name="saveImmediately">是否立即保存配置文件</param>
    public void AddPlayTime(long seconds, bool saveImmediately)
    {
        Config.PlayTimeSeconds += seconds;
        if (saveImmediately)
        {
            SaveConfig();
        }
        InstanceManager.Instance.NotifyStatisticsChanged();
    }

    /// <summary>
    /// 增加游戏会话次数
    /// </summary>
    public void IncrementPlaySessions()
    {
        Config.PlaySessions++;
        SaveConfig();
        InstanceManager.Instance.NotifyStatisticsChanged();
    }

    private System.Threading.Timer? _playTimer;
    private readonly object _timerLock = new();
    private long _unsavedSeconds;

    /// <summary>
    /// 开始计时（用于实时更新游玩时长）
    /// 使用低资源占用的 Timer，每秒触发一次，但每60秒才保存一次配置文件
    /// UI会实时更新显示，但配置文件只在间隔时间保存
    /// </summary>
    public void StartPlayTimer()
    {
        lock (_timerLock)
        {
            if (_playTimer != null)
                return;

            _playTimer = new Timer(
                _ =>
                {
                    lock (_timerLock)
                    {
                        _unsavedSeconds++;
                        InstanceManager.Instance.NotifyStatisticsChanged();
                        
                        if (_unsavedSeconds >= 60)
                        {
                            Config.PlayTimeSeconds += _unsavedSeconds;
                            _unsavedSeconds = 0;
                            SaveConfig();
                        }
                    }
                },
                null,
                0,
                1000
            );
        }
    }

    /// <summary>
    /// 停止计时，立即保存所有未保存的秒数
    /// </summary>
    public void StopPlayTimer()
    {
        lock (_timerLock)
        {
            _playTimer?.Dispose();
            _playTimer = null;

            if (_unsavedSeconds > 0)
            {
                Config.PlayTimeSeconds += _unsavedSeconds;
                _unsavedSeconds = 0;
                SaveConfig();
                InstanceManager.Instance.NotifyStatisticsChanged();
            }
        }
    }

    /// <summary>
    /// 获取当前实例的总游玩时长（秒），包括已保存和未保存的秒数
    /// </summary>
    public long GetTotalPlayTimeSeconds()
    {
        lock (_timerLock)
        {
            return Config.PlayTimeSeconds + _unsavedSeconds;
        }
    }

    public string GetSpecialFolder(MinecraftSpecialFolder folder)
    {
        if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
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

        return InstanceFolderPath;
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

        if (Type == MinecraftInstanceType.Bedrock)
        {
            return LoadBitmapFromAssembly("grass_block_side.png");
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
        if (Type == MinecraftInstanceType.Bedrock)
        {
            return "grass_block_side.png";
        }

        if (MinecraftEntry == null) return "grass_block_side.png";

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
    [ObservableProperty] public partial DateTime LastPlayTime { get; set; } = DateTime.MinValue;
    [ObservableProperty] public partial long PlayTimeSeconds { get; set; }
    [ObservableProperty] public partial int PlaySessions { get; set; }
}

public enum MinecraftInstanceType
{
    Java,
    Bedrock
}