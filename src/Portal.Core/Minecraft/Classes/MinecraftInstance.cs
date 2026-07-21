using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Models.Game;
using Newtonsoft.Json;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using MinecraftLaunch.Base.Enums;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Java;

namespace Portal.Core.Minecraft.Classes;

public class MinecraftInstance : ObservableObject
{
    public MinecraftInstanceType Type { get; init; }

    public MinecraftEntry? MinecraftEntry { get; init; }

    public BedrockInstanceConfig? BedrockConfig { get; init; }

    public string FolderName { get; init; }
    public string FolderPath { get; init; }

    public string InstanceFolderPath { get; init; }
    public MinecraftInstanceLayout? Layout { get; init; }
    public string? ExternalDisplayName { get; init; }
    public string FolderTypeDescription => Layout?.KindDisplayName ?? "传统 .minecraft";
    public bool IsExternallyManaged => Layout != null;
    public bool RequiresIndependentInstance => Layout?.Kind is
        MinecraftFolderKind.ModrinthApp or MinecraftFolderKind.ModrinthProfile or
        MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance or
        MinecraftFolderKind.BakaXl or MinecraftFolderKind.BakaXlInstance or
        MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance;
    public bool CanDisableIndependentInstance => !RequiresIndependentInstance;

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
                return Layout?.InstanceRoot ?? Path.GetDirectoryName(MinecraftEntry.ClientJarPath);
            return InstanceFolderPath;
        }
    }

    public bool IsJava => Type == MinecraftInstanceType.Java;
    public bool IsBedrock => Type == MinecraftInstanceType.Bedrock;

    public bool EnableIndependentBedrockVersion
    {
        get => BedrockConfig?.EnableIndependentInstance ?? false;
        set => UpdateBedrockDataSetting(nameof(EnableIndependentBedrockVersion), config =>
            config.EnableIndependentInstance = value);
    }

    public bool EnableLauncherSharedBedrockData
    {
        get => BedrockConfig?.EnableLauncherSharedData ?? false;
        set => UpdateBedrockDataSetting(nameof(EnableLauncherSharedBedrockData), config =>
            config.EnableLauncherSharedData = value);
    }

    public string BedrockDataScope => (EnableIndependentBedrockVersion, EnableLauncherSharedBedrockData) switch
    {
        (true, false) => "Portal 实例隔离数据文件夹",
        (false, false) => "Portal 数据文件夹",
        (true, true) => "用户目录共享文件夹",
        (false, true) => "实例隔离数据文件夹"
    };

    public string InstanceName
    {
        get
        {
            string id;
            if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
                id = ExternalDisplayName ?? MinecraftEntry.Id;
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

    [JsonIgnore]
    public JavaInstanceConfig? JavaConfig => Config as JavaInstanceConfig;

    [JsonIgnore] public InstanceStorageUsage StorageUsage => field ??= new InstanceStorageUsage(this);

    public Bitmap Icon => _icon ??= GetInstanceIcon(48);
    private Bitmap? _icon;

    // This is deliberately not resized so it can be exported without losing detail.
    public Bitmap sourceIcon => _sourceIcon ??= GetSourceIcon();
    private Bitmap? _sourceIcon;

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
                var playTime = GetTotalPlayTimeSeconds();
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
        : this(e, null)
    {
    }

    public MinecraftInstance(MinecraftEntry e, MinecraftInstanceLayout? layout)
    {
        Type = MinecraftInstanceType.Java;
        MinecraftEntry = e;
        Layout = layout;
        InstanceFolderPath = layout?.InstanceRoot ?? e.VersionDirectoryPath ??
                             Path.Combine(e.MinecraftFolderPath, "versions", e.Id);
        Config = GetInstanceConfig();
        EnsureRequiredIndependentInstance();
        ObserveConfigChanges();
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
        ObserveConfigChanges();
    }

    private void ObserveConfigChanges()
    {
        Config.PropertyChanged += (_, e) =>
        {
            SaveConfig();
            OnPropertyChanged(e.PropertyName);

            if (e.PropertyName == nameof(MinecraftInstanceConfig.LastPlayTime))
            {
                OnPropertyChanged(nameof(DisplayLastPlayTime));
                OnPropertyChanged(nameof(FullInfo));
            }

            if (e.PropertyName == nameof(MinecraftInstanceConfig.Note))
            {
                OnPropertyChanged(nameof(InstanceName));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(FullInfo));
            }

            if (e.PropertyName == nameof(JavaInstanceConfig.EnableIndependentInstance))
            {
                StorageUsage.Refresh();
                OnPropertyChanged(nameof(StorageUsage));
            }
        };
    }

    private void UpdateBedrockDataSetting(string propertyName, Action<BedrockInstanceConfig> update)
    {
        if (BedrockConfig == null)
            return;

        update(BedrockConfig);
        BedrockHelper.SaveInstanceConfig(BedrockConfig);
        StorageUsage.Refresh();
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(BedrockDataScope));
        OnPropertyChanged(nameof(StorageUsage));
    }

    private MinecraftInstanceConfig GetInstanceConfig()
    {
        var configPath = GetConfigPath();
        if (File.Exists(configPath))
        {
            var loadedConfig = Type == MinecraftInstanceType.Java
                ? JsonConvert.DeserializeObject<JavaInstanceConfig>(File.ReadAllText(configPath))!
                : JsonConvert.DeserializeObject<MinecraftInstanceConfig>(File.ReadAllText(configPath))!;
            if (loadedConfig != null)
                return loadedConfig;
        }

        MinecraftInstanceConfig config = Type == MinecraftInstanceType.Java
            ? new JavaInstanceConfig()
            : new MinecraftInstanceConfig();
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
        return config;
    }

    public void SaveConfig()
    {
        lock (_timerLock)
        {
            EnsureRequiredIndependentInstance();
            FormatPlayTimeData();
            var configPath = GetConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
        }
    }

    private void EnsureRequiredIndependentInstance()
    {
        if (RequiresIndependentInstance && JavaConfig?.EnableIndependentInstance == false)
            JavaConfig.EnableIndependentInstance = true;
    }

    private string GetConfigPath()
    {
        if (Layout == null)
            return Path.Combine(MinecraftPath, "Portal.config.json");

        var identity = $"{Layout.Kind}|{Path.GetFullPath(Layout.InstanceRoot)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "xyz.tiouo.Portal", "Instances", $"{hash}.json");
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
        AddPlayTimeForDate(DateTime.Today, seconds);
        if (saveImmediately)
        {
            SaveConfig();
        }

        InstanceManager.Instance.NotifyStatisticsChanged();
        OnPropertyChanged(nameof(FullInfo));
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
    private readonly Dictionary<string, long> _unsavedPlayTimeByDate = [];

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
                        AddUnsavedPlayTime(DateTime.Today, 1);
                        OnPropertyChanged(nameof(FullInfo));
                        InstanceManager.Instance.NotifyStatisticsChanged();

                        if (_unsavedPlayTimeByDate.Values.Sum() >= 60)
                        {
                            SaveUnsavedPlayTime();
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

            if (_unsavedPlayTimeByDate.Count > 0)
            {
                SaveUnsavedPlayTime();
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
            return Config.ArchivedPlayTimeSeconds
                   + Config.LegacyPlayTimeSeconds
                   + GetDailyPlayTimeByDate().Values.Sum()
                   + _unsavedPlayTimeByDate.Values.Sum();
        }
    }

    /// <summary>
    /// 获取包含今天在内的最近每日游玩时长；没有记录的日期以零补齐。
    /// </summary>
    public IReadOnlyList<(DateTime Date, long Seconds)> GetRecentDailyPlayTime(int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        lock (_timerLock)
        {
            var playTimeByDate = GetDailyPlayTimeByDate();
            return Enumerable.Range(0, days)
                .Select(offset => DateTime.Today.AddDays(offset - days + 1))
                .Select(date =>
                {
                    var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return (date,
                        playTimeByDate.GetValueOrDefault(key) + _unsavedPlayTimeByDate.GetValueOrDefault(key));
                })
                .ToArray();
        }
    }

    private void AddPlayTimeForDate(DateTime date, long seconds)
    {
        if (seconds <= 0)
            return;

        var playTimeByDate = GetDailyPlayTimeByDate();
        var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        playTimeByDate[key] = playTimeByDate.GetValueOrDefault(key) + seconds;
    }

    private void AddUnsavedPlayTime(DateTime date, long seconds)
    {
        var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _unsavedPlayTimeByDate[key] = _unsavedPlayTimeByDate.GetValueOrDefault(key) + seconds;
    }

    private void SaveUnsavedPlayTime()
    {
        foreach (var (date, seconds) in _unsavedPlayTimeByDate)
        {
            var playTimeByDate = GetDailyPlayTimeByDate();
            playTimeByDate[date] = playTimeByDate.GetValueOrDefault(date) + seconds;
        }

        _unsavedPlayTimeByDate.Clear();
    }

    private Dictionary<string, long> GetDailyPlayTimeByDate()
    {
        return Config.PlayTimeByDate ??= [];
    }

    /// <summary>
    /// 将旧版总时长迁移到历史汇总，并将一个月前的日记录合并，控制配置文件大小。
    /// </summary>
    private void FormatPlayTimeData()
    {
        if (Config.LegacyPlayTimeSeconds > 0)
        {
            Config.ArchivedPlayTimeSeconds += Config.LegacyPlayTimeSeconds;
            Config.LegacyPlayTimeSeconds = 0;
        }

        var cutoffDate = DateTime.Today.AddMonths(-1);
        var playTimeByDate = GetDailyPlayTimeByDate();
        foreach (var (date, seconds) in playTimeByDate.ToArray())
        {
            if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day) && day < cutoffDate)
            {
                Config.ArchivedPlayTimeSeconds += seconds;
                playTimeByDate.Remove(date);
            }
        }
    }

    [JsonIgnore] public IconSizeProxy Icons => field ??= new IconSizeProxy(this);

    public class IconSizeProxy(MinecraftInstance instance)
    {
        public Bitmap this[int width] => instance.GetInstanceIcon(width);
    }

    public string GetSpecialFolder(MinecraftSpecialFolder folder)
    {
        if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
        {
            var instancePath = Layout?.InstanceRoot ?? MinecraftEntry.VersionDirectoryPath ??
                               Path.Combine(MinecraftEntry.MinecraftFolderPath, "versions", MinecraftEntry.Id);
            var basePath = Layout?.GameDirectory ?? (JavaConfig?.EnableIndependentInstance == true
                ? instancePath
                : MinecraftEntry.MinecraftFolderPath);
            var path = folder switch
            {
                MinecraftSpecialFolder.InstanceFolder => instancePath,
                MinecraftSpecialFolder.ModsFolder => Path.Combine(basePath, "mods"),
                MinecraftSpecialFolder.ResourcePacksFolder => Path.Combine(basePath, "resourcepacks"),
                MinecraftSpecialFolder.SavesFolder => Path.Combine(basePath, "saves"),
                MinecraftSpecialFolder.ScreenshotsFolder => Path.Combine(basePath, "screenshots"),
                MinecraftSpecialFolder.ShaderPacksFolder => Path.Combine(basePath, "shaderpacks"),
                MinecraftSpecialFolder.ConfigFolder => Path.Combine(basePath, "config"),
                MinecraftSpecialFolder.LogsFolder => Path.Combine(basePath, "logs"),
                _ => basePath
            };

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        if (Type == MinecraftInstanceType.Bedrock && BedrockConfig != null)
        {
            var path = BedrockDataPathResolver.GetFolder(BedrockConfig, folder);
            if (folder != MinecraftSpecialFolder.InstanceFolder && !Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        return InstanceFolderPath;
    }

    public void SetIcon(Bitmap icon)
    {
        var instanceFolder = GetIconOverrideFolder();
        Directory.CreateDirectory(instanceFolder);
        using (var stream = File.Create(Path.Combine(instanceFolder, "Icon.png")))
        {
            icon.Save(stream, PngBitmapEncoderOptions.Default);
        }

        RefreshIcon();
    }

    public void ResetIcon()
    {
        var instanceFolder = GetIconOverrideFolder();
        foreach (var iconPath in new[]
                 {
                     Path.Combine(instanceFolder, "Icon.png"),
                      Path.Combine(instanceFolder, "icon.png")
                 }.Distinct(StringComparer.Ordinal))
        {
            if (File.Exists(iconPath))
                File.Delete(iconPath);
        }

        RefreshIcon();
    }

    private void RefreshIcon()
    {
        _icon?.Dispose();
        _icon = null;
        _sourceIcon?.Dispose();
        _sourceIcon = null;
        OnPropertyChanged(nameof(sourceIcon));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Icons));
        InstanceManager.Instance.NotifyInstanceIconChanged(this);
    }

    private Bitmap GetSourceIcon()
    {
        var instanceFolder = GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder);
        var customIcon = GetCustomIconPath(GetIconOverrideFolder()) ?? GetCustomIconPath(instanceFolder);
        if (customIcon != null)
            return new Bitmap(customIcon);

        if (Layout?.NativeIconPath is { } nativeIcon && File.Exists(nativeIcon))
            return new Bitmap(nativeIcon);

        if (Type == MinecraftInstanceType.Bedrock)
            return LoadBitmapFromAssembly("01_grass_block_side.png");

        var pclIcon = Path.Combine(instanceFolder, "PCL", "Logo.png");
        if (File.Exists(pclIcon))
            return new Bitmap(pclIcon);

        var iconName = GetEmbeddedIconName();
        return LoadBitmapFromAssembly(iconName);
    }

    public Bitmap this[int width] => GetInstanceIcon(width);

    private Bitmap GetInstanceIcon(int width)
    {
        var instanceFolder = GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder);
        var customIcon = GetCustomIconPath(GetIconOverrideFolder()) ?? GetCustomIconPath(instanceFolder);
        if (customIcon != null)
        {
            using var s = File.OpenRead(customIcon);
            return Bitmap.DecodeToWidth(s, width);
        }

        if (Layout?.NativeIconPath is { } nativeIcon && File.Exists(nativeIcon))
        {
            using var s = File.OpenRead(nativeIcon);
            return Bitmap.DecodeToWidth(s, width);
        }

        if (Type == MinecraftInstanceType.Bedrock)
        {
            return LoadBitmapFromAssembly("01_grass_block_side.png", width);
        }

        var pclIcon = Path.Combine(instanceFolder, "PCL", "Logo.png");
        if (File.Exists(pclIcon))
        {
            using var s = File.OpenRead(pclIcon);
            return Bitmap.DecodeToWidth(s, width);
        }

        var iconName = GetEmbeddedIconName();
        return LoadBitmapFromAssembly(iconName, width);
    }

    private static string? GetCustomIconPath(string instanceFolder)
    {
        var iconPath = Path.Combine(instanceFolder, "Icon.png");
        if (File.Exists(iconPath))
            return iconPath;

        var legacyIconPath = Path.Combine(instanceFolder, "icon.png");
        return File.Exists(legacyIconPath) ? legacyIconPath : null;
    }

    private string GetIconOverrideFolder()
    {
        if (Layout == null)
            return GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder);
        return Path.Combine(Path.GetDirectoryName(GetConfigPath())!, Path.GetFileNameWithoutExtension(GetConfigPath()));
    }

    private static Bitmap LoadBitmapFromAssembly(string fileName, int width)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePath = $"Portal.Core.Assets.McIcons.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream == null)
        {
            // 修正了你原代码里的一处拼写错误：Assts -> Assets
            var defaultPath = "Portal.Core.Assets.McIcons.01_grass_block_side.png";
            using var defaultStream = assembly.GetManifestResourceStream(defaultPath);
            return defaultStream != null ? Bitmap.DecodeToWidth(defaultStream, width) : null;
        }

        return Bitmap.DecodeToWidth(stream, width);
    }

    private string GetEmbeddedIconName()
    {
        if (Type == MinecraftInstanceType.Bedrock)
        {
            return "01_grass_block_side.png";
        }

        if (MinecraftEntry == null) return "01_grass_block_side.png";

        if (MinecraftEntry.IsVanilla)
        {
            return MinecraftEntry.Version.Type switch
            {
                MinecraftVersionType.Snapshot => "02_crafting_table_front.png",
                _ => "01_grass_block_side.png"
            };
        }

        if (MinecraftEntry is ModifiedMinecraftEntry e && e.ModLoaders != null)
        {
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.Forge)) return "06_ForgeIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.NeoForge)) return "07_NeoForgeIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.Fabric)) return "05_FabricIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.Quilt)) return "09_QuiltIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.OptiFine)) return "08_OptiFineIcon.png";
        }

        return "01_grass_block_side.png";
    }

    private static Bitmap LoadBitmapFromAssembly(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePath = $"Portal.Core.Assets.McIcons.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream == null)
        {
            var defaultPath = "Portal.Core.Assets.McIcons.01_grass_block_side.png";
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
    [ObservableProperty] public partial DateTime LastPlayTime { get; set; } = DateTime.MinValue;

    [ObservableProperty]
    public partial Dictionary<string, long> PlayTimeByDate { get; set; } = []; //string : Data (yyyy-MM-dd)

    public bool ShouldSerializePlayTimeByDate() => PlayTimeByDate?.Count > 0;
    public long ArchivedPlayTimeSeconds { get; set; }

    [JsonProperty("PlayTimeSeconds", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long LegacyPlayTimeSeconds { get; set; }

    [ObservableProperty] public partial int PlaySessions { get; set; }
}

/// <summary>
/// Java 版实例独有的启动与运行时配置。
/// </summary>
public partial class JavaInstanceConfig : MinecraftInstanceConfig
{
    [ObservableProperty] public partial bool EnableIndependentInstance { get; set; } = true;
    [ObservableProperty] public partial bool EnableSpecificJava { get; set; }
    [ObservableProperty] public partial bool EnableOverrideMaxMemory { get; set; }
    [ObservableProperty] public partial int MinecraftMaxMemory { get; set; }
    [ObservableProperty] public partial JavaRuntimeEntry? SpecificJavaEntry { get; set; }
}

public enum MinecraftInstanceType
{
    Java,
    Bedrock
}
