using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Classes;
using Portal.Core.Json;
using Portal.Core.Minecraft.Graphics;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Classes;

public class MinecraftInstance : ObservableObject
{
    public const string PortablePortalConfigFileName = "Portal.config.json";

    public const string PortablePortalIconFileName = "Portal.Icon.png";

    private readonly Dictionary<int, Bitmap> _iconsByWidth = [];
    private readonly object _timerLock = new();
    private readonly Dictionary<string, long> _unsavedPlayTimeByDate = [];
    private Bitmap? _icon;

    private bool _isBlocked;

    private Timer? _playTimer;
    private Bitmap? _sourceIcon;

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

    public MinecraftInstance(BedrockInstanceConfig bedrockConfig, string folderName, string folderPath)
    {
        Type = MinecraftInstanceType.Bedrock;
        BedrockConfig = bedrockConfig;
        FolderName = folderName;
        FolderPath = folderPath;
        InstanceFolderPath = bedrockConfig.InstancePath;
        ObserveConfigChanges();
    }

    public MinecraftInstanceType Type { get; init; }

    [JsonIgnore]
    public bool IsBlocked
    {
        get => _isBlocked;
        set
        {
            if (SetProperty(ref _isBlocked, value))
                OnPropertyChanged(nameof(BlockHeaderText));
        }
    }

    public string BlockHeaderText => IsBlocked ? CommonLanguageManager.Instance.minecraft_unblock.CurrentValue() : CommonLanguageManager.Instance.minecraft_block.CurrentValue();
    public string FavoriteHeaderText => Config?.IsFavorite == true ? CommonLanguageManager.Instance.minecraft_unfavorite.CurrentValue() : CommonLanguageManager.Instance.minecraft_favorite.CurrentValue();

    public MinecraftEntry? MinecraftEntry { get; init; }

    public BedrockInstanceConfig? BedrockConfig { get; init; }

    public string FolderName { get; init; }
    public string FolderPath { get; init; }

    public string InstanceFolderPath { get; init; }
    public MinecraftInstanceLayout? Layout { get; init; }
    public string? ExternalDisplayName { get; init; }
    public string FolderTypeDescription => Layout?.KindDisplayName ?? CommonLanguageManager.Instance.minecraft_traditionalMinecraft.CurrentValue();
    public bool IsExternallyManaged => Layout != null;

    public bool RequiresIndependentInstance => Layout?.Kind is
        MinecraftFolderKind.Modrinth or MinecraftFolderKind.ModrinthInstance or
        MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance or
        MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance or
        MinecraftFolderKind.PortalMc;

    public bool CanDisableIndependentInstance => !RequiresIndependentInstance;

    public bool CanModifyVersion =>
        Type == MinecraftInstanceType.Java && MinecraftEntry is not null &&
        Layout?.Kind == MinecraftFolderKind.PortalMc;

    public DateTime LastPlayTime => Config?.LastPlayTime ?? DateTime.MinValue;

    [JsonIgnore]
    public string DisplayLastPlayTime
    {
        get
        {
            var time = LastPlayTime;
            if (time == DateTime.MinValue)
                return CommonLanguageManager.Instance.minecraft_neverPlayed.CurrentValue();

            var timeSpan = DateTime.Now - time;

            if (timeSpan.TotalMinutes < 1)
                return CommonLanguageManager.Instance.minecraft_justNow.CurrentValue();

            if (!(timeSpan.TotalDays <= 30)) return time.ToString("yyyy-MM-dd HH:mm");
            if (timeSpan.TotalDays >= 1)
                return string.Format(CommonLanguageManager.Instance.minecraft_daysAgo.CurrentValue(), (int)timeSpan.TotalDays);

            return timeSpan.TotalHours >= 1
                ? string.Format(CommonLanguageManager.Instance.minecraft_hoursAgo.CurrentValue(), (int)timeSpan.TotalHours)
                : string.Format(CommonLanguageManager.Instance.minecraft_minutesAgo.CurrentValue(), (int)timeSpan.TotalMinutes);
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
        (true, false) => CommonLanguageManager.Instance.minecraft_bedrockScopePortalInstance.CurrentValue(),
        (false, false) => CommonLanguageManager.Instance.minecraft_bedrockScopePortal.CurrentValue(),
        (true, true) => CommonLanguageManager.Instance.minecraft_bedrockScopeInstance.CurrentValue(),
        (false, true) => CommonLanguageManager.Instance.minecraft_bedrockScopeUserShared.CurrentValue()
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

    [JsonIgnore] public JavaInstanceConfig? JavaConfig => Config as JavaInstanceConfig;

    [JsonIgnore] public InstanceStorageUsage StorageUsage => field ??= new InstanceStorageUsage(this);

    public Bitmap Icon => _icon ??= GetInstanceIcon(48);

    public Bitmap sourceIcon => _sourceIcon ??= GetSourceIcon();

    public string LoaderDescription
    {
        get
        {
            if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
                return MinecraftEntry.IsVanilla ||
                       (MinecraftEntry as ModifiedMinecraftEntry)?.ModLoaders.Any() == false
                    ? CommonLanguageManager.Instance.minecraft_vanilla.CurrentValue()
                    : string.Join(", ", (MinecraftEntry as ModifiedMinecraftEntry)?
                        .ModLoaders.Select(x => x.Type.ToString()) ?? []);

            if (Type == MinecraftInstanceType.Bedrock) return CommonLanguageManager.Instance.minecraft_bedrock.CurrentValue();

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
                info.Add(string.Format(CommonLanguageManager.Instance.minecraft_fullInfoNote.CurrentValue(), note));

            if (!string.IsNullOrEmpty(FolderName))
                info.Add(string.Format(CommonLanguageManager.Instance.minecraft_fullInfoFolder.CurrentValue(), FolderName));

            if (!string.IsNullOrEmpty(LoaderDescription))
                info.Add(string.Format(CommonLanguageManager.Instance.minecraft_fullInfoLoader.CurrentValue(), LoaderDescription));

            if (!string.IsNullOrEmpty(VersionId))
                info.Add(string.Format(CommonLanguageManager.Instance.minecraft_fullInfoVersion.CurrentValue(), VersionId));

            if (!string.IsNullOrEmpty(VersionType))
                info.Add(string.Format(CommonLanguageManager.Instance.minecraft_fullInfoType.CurrentValue(), VersionType));

            if (!string.IsNullOrEmpty(DisplayLastPlayTime))
                info.Add(string.Format(CommonLanguageManager.Instance.minecraft_fullInfoLastPlayed.CurrentValue(), DisplayLastPlayTime));

            if (Config != null)
            {
                var playTime = GetTotalPlayTimeSeconds();
                if (playTime > 0)
                {
                    string timeStr;
                    if (playTime < 60)
                        timeStr = string.Format(CommonLanguageManager.Instance.minecraft_playTimeSeconds.CurrentValue(), playTime);
                    else if (playTime < 3600)
                        timeStr = string.Format(CommonLanguageManager.Instance.minecraft_playTimeMinutes.CurrentValue(), $"{playTime / 60.0:F1}");
                    else
                        timeStr = string.Format(CommonLanguageManager.Instance.minecraft_playTimeHours.CurrentValue(), $"{playTime / 3600.0:F1}");
                    info.Add(string.Format(CommonLanguageManager.Instance.minecraft_fullInfoPlayTime.CurrentValue(), timeStr));
                }

                if (Config.PlaySessions > 0)
                    info.Add(string.Format(CommonLanguageManager.Instance.minecraft_fullInfoSessions.CurrentValue(), Config.PlaySessions));
            }

            return string.Join("\n", info);
        }
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

    [JsonIgnore] public IconSizeProxy Icons => field ??= new IconSizeProxy(this);

    public Bitmap this[int width] => GetInstanceIcon(width);

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

            if (e.PropertyName == nameof(MinecraftInstanceConfig.IsFavorite))
                OnPropertyChanged(nameof(FavoriteHeaderText));
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
            try
            {
                var loadedConfig = Type == MinecraftInstanceType.Java
                    ? JsonSerializer.Deserialize<JavaInstanceConfig>(File.ReadAllText(configPath), PortalJson.Options)
                    : JsonSerializer.Deserialize<MinecraftInstanceConfig>(File.ReadAllText(configPath), PortalJson.Options);
                if (loadedConfig != null)
                    return loadedConfig;
            }
            catch (Exception e)
            {
                Logger.Error(string.Format(LogLanguageManager.Instance.minecraft_instanceConfigLoadFailed.CurrentValue(), configPath), e);
                try
                {
                    File.Copy(configPath, configPath + ".bak", true);
                }
                catch (Exception backupException)
                {
                    Logger.Error(LogLanguageManager.Instance.minecraft_instanceConfigBackupFailed.CurrentValue(), backupException);
                }
            }

        var config = Type == MinecraftInstanceType.Java
            ? new JavaInstanceConfig()
            : new MinecraftInstanceConfig();
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, config.GetType(), PortalJson.Options));
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
            File.WriteAllText(configPath, JsonSerializer.Serialize(Config, Config.GetType(), PortalJson.Options));
        }
    }

    private void EnsureRequiredIndependentInstance()
    {
        if (RequiresIndependentInstance && JavaConfig?.EnableIndependentInstance == false)
            JavaConfig.EnableIndependentInstance = true;
    }

    public string GetConfigPath()
    {
        if (Layout == null)
            return Path.Combine(MinecraftPath, "Portal.config.json");

        return GetExternalConfigPath(Layout.Kind, Layout.InstanceRoot);
    }

    public static string GetExternalConfigPath(MinecraftFolderKind kind, string instanceRoot)
    {
        var identity = $"{kind}|{Path.GetFullPath(instanceRoot)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "cc.tiouo.Portal", "Instances", $"{hash}.json");
    }

    public IReadOnlyList<string> GetDeletionPaths()
    {
        var paths = new List<string> { InstanceFolderPath };

        if (Layout is not { } layout)
            return paths;

        if (layout.Kind == MinecraftFolderKind.PortalMc && MinecraftEntry is { } entry)
        {
            var nativesDirectory = Path.Combine(layout.MetadataRoot, "natives", entry.Id);
            if (Directory.Exists(nativesDirectory))
                paths.Add(nativesDirectory);
        }

        var instanceDataDirectory = Path.GetDirectoryName(GetConfigPath());
        if (instanceDataDirectory is not null && Directory.Exists(instanceDataDirectory))
            paths.Add(instanceDataDirectory);

        return paths;
    }

    public void AddPlayTime(long seconds)
    {
        AddPlayTime(seconds, true);
    }

    public void AddPlayTime(long seconds, bool saveImmediately)
    {
        lock (_timerLock)
        {
            AddPlayTimeForDate(DateTime.Today, seconds);
            if (saveImmediately) SaveConfig();
        }

        NotifyPlayTimeChanged();
    }

    public void IncrementPlaySessions()
    {
        Config.PlaySessions++;
        SaveConfig();
        Dispatcher.UIThread.Post(InstanceManager.Instance.NotifyStatisticsChanged);
    }

    private void NotifyPlayTimeChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(FullInfo));
            InstanceManager.Instance.NotifyStatisticsChanged();
        });
    }

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

                        if (_unsavedPlayTimeByDate.Values.Sum() >= 60)
                        {
                            SaveUnsavedPlayTime();
                            SaveConfig();
                        }
                    }

                    NotifyPlayTimeChanged();
                },
                null,
                0,
                1000
            );
        }
    }

    public void StopPlayTimer()
    {
        var changed = false;
        lock (_timerLock)
        {
            _playTimer?.Dispose();
            _playTimer = null;

            if (_unsavedPlayTimeByDate.Count > 0)
            {
                SaveUnsavedPlayTime();
                SaveConfig();
                changed = true;
            }
        }

        if (changed)
            NotifyPlayTimeChanged();
    }

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
            if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day) && day < cutoffDate)
            {
                Config.ArchivedPlayTimeSeconds += seconds;
                playTimeByDate.Remove(date);
            }
    }

    public string GetJavaGameDirectory()
    {
        if (Type != MinecraftInstanceType.Java || MinecraftEntry == null)
            return InstanceFolderPath;

        return Layout?.GameDirectory is { Length: > 0 } gameDirectory
            ? gameDirectory
            : JavaConfig?.EnableIndependentInstance == true
                ? GetJavaInstanceFolder()
                : MinecraftEntry.MinecraftFolderPath;
    }

    private string GetJavaInstanceFolder()
    {
        if (Layout?.InstanceRoot is { Length: > 0 } instanceRoot)
            return instanceRoot;

        return MinecraftEntry!.VersionDirectoryPath ??
               Path.Combine(MinecraftEntry.MinecraftFolderPath, "versions", MinecraftEntry.Id);
    }

    public string GetSpecialFolder(MinecraftSpecialFolder folder)
    {
        if (Type == MinecraftInstanceType.Java && MinecraftEntry != null)
        {
            var path = folder switch
            {
                MinecraftSpecialFolder.InstanceFolder => GetJavaInstanceFolder(),
                MinecraftSpecialFolder.ModsFolder => Path.Combine(GetJavaGameDirectory(), "mods"),
                MinecraftSpecialFolder.ResourcePacksFolder => Path.Combine(GetJavaGameDirectory(), "resourcepacks"),
                MinecraftSpecialFolder.SavesFolder => Path.Combine(GetJavaGameDirectory(), "saves"),
                MinecraftSpecialFolder.ScreenshotsFolder => Path.Combine(GetJavaGameDirectory(), "screenshots"),
                MinecraftSpecialFolder.ShaderPacksFolder => Path.Combine(GetJavaGameDirectory(), "shaderpacks"),
                MinecraftSpecialFolder.ConfigFolder => Path.Combine(GetJavaGameDirectory(), "config"),
                MinecraftSpecialFolder.LogsFolder => Path.Combine(GetJavaGameDirectory(), "logs"),
                MinecraftSpecialFolder.CrashReportsFolder => Path.Combine(GetJavaGameDirectory(), "crash-reports"),
                _ => GetJavaGameDirectory()
            };

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

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
        var iconPath = GetIconOverridePath();
        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
        using (var stream = File.Create(iconPath))
        {
            icon.Save(stream, PngBitmapEncoderOptions.Default);
        }

        RefreshIcon();
    }

    public void ResetIcon()
    {
        foreach (var iconPath in GetIconOverridePaths())
            if (File.Exists(iconPath))
                File.Delete(iconPath);

        RefreshIcon();
    }

    private void RefreshIcon()
    {
        _icon = null;
        _sourceIcon = null;

        lock (_iconsByWidth)
        {
            _iconsByWidth.Clear();
        }

        OnPropertyChanged(nameof(sourceIcon));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Icons));
        InstanceManager.Instance.NotifyInstanceIconChanged(this);
    }

    private Bitmap GetSourceIcon()
    {
        var instanceFolder = GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder);
        var customIcon = GetCustomIconPath();
        if (customIcon != null)
            return new Bitmap(customIcon);

        var nativeIcon = GetNativeIconPath(instanceFolder);
        if (nativeIcon != null)
            return new Bitmap(nativeIcon);

        if (Type == MinecraftInstanceType.Bedrock)
            return LoadBitmapFromAssembly("01_grass_block_side.png");

        var pclIcon = Path.Combine(instanceFolder, "PCL", "Logo.png");
        if (File.Exists(pclIcon))
            return new Bitmap(pclIcon);

        var iconName = GetEmbeddedIconName();
        return LoadBitmapFromAssembly(iconName);
    }

    private Bitmap GetInstanceIcon(int width)
    {
        lock (_iconsByWidth)
        {
            if (_iconsByWidth.TryGetValue(width, out var cached))
                return cached;
        }

        var icon = DecodeInstanceIcon(width);

        lock (_iconsByWidth)
        {
            if (_iconsByWidth.TryGetValue(width, out var existing))
            {
                icon.Dispose();
                return existing;
            }

            _iconsByWidth[width] = icon;
            return icon;
        }
    }

    private Bitmap DecodeInstanceIcon(int width)
    {
        var instanceFolder = GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder);
        var customIcon = GetCustomIconPath();
        if (customIcon != null)
        {
            using var s = File.OpenRead(customIcon);
            return Bitmap.DecodeToWidth(s, width);
        }

        var nativeIcon = GetNativeIconPath(instanceFolder);
        if (nativeIcon != null)
        {
            using var s = File.OpenRead(nativeIcon);
            return Bitmap.DecodeToWidth(s, width);
        }

        if (Type == MinecraftInstanceType.Bedrock) return LoadBitmapFromAssembly("01_grass_block_side.png", width);

        var pclIcon = Path.Combine(instanceFolder, "PCL", "Logo.png");
        if (File.Exists(pclIcon))
        {
            using var s = File.OpenRead(pclIcon);
            return Bitmap.DecodeToWidth(s, width);
        }

        var iconName = GetEmbeddedIconName();
        return LoadBitmapFromAssembly(iconName, width);
    }

    private string? GetCustomIconPath()
    {
        return GetIconOverridePaths().FirstOrDefault(File.Exists);
    }

    public string? GetExportIconPath()
    {
        var customIcon = GetCustomIconPath();
        if (customIcon != null)
            return customIcon;

        var instanceFolder = GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder);
        var nativeIcon = GetNativeIconPath(instanceFolder);
        if (nativeIcon != null)
            return nativeIcon;

        var pclIcon = Path.Combine(instanceFolder, "PCL", "Logo.png");
        if (File.Exists(pclIcon))
            return pclIcon;

        return null;
    }

    private IEnumerable<string> GetIconOverridePaths()
    {
        yield return GetIconOverridePath();

        if (Layout != null)
            yield return Path.Combine(Path.GetDirectoryName(GetConfigPath())!,
                Path.GetFileNameWithoutExtension(GetConfigPath()),
                "Icon.png");
        else
            yield return Path.Combine(GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder), "Icon.png");
    }

    private string GetIconOverridePath()
    {
        if (Layout == null)
            return Path.Combine(GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder), "Portal.Icon.png");
        return GetConfigPath() + ".png";
    }

    private string? GetNativeIconPath(string instanceFolder)
    {
        if (Layout?.NativeIconPath is { } nativeIcon && File.Exists(nativeIcon))
            return nativeIcon;

        if (Layout == null)
        {
            var iconPath = Path.Combine(instanceFolder, "icon.png");
            if (File.Exists(iconPath))
                return iconPath;
        }

        return null;
    }

    private static Bitmap LoadBitmapFromAssembly(string fileName, int width)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePath = $"Portal.Core.Assets.McIcons.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream == null)
        {
            var defaultPath = "Portal.Core.Assets.McIcons.01_grass_block_side.png";
            using var defaultStream = assembly.GetManifestResourceStream(defaultPath);
            return defaultStream != null ? Bitmap.DecodeToWidth(defaultStream, width) : null;
        }

        return Bitmap.DecodeToWidth(stream, width);
    }

    private string GetEmbeddedIconName()
    {
        if (Type == MinecraftInstanceType.Bedrock) return "01_grass_block_side.png";

        if (MinecraftEntry == null) return "01_grass_block_side.png";

        if (MinecraftEntry is ModifiedMinecraftEntry e && e.ModLoaders.Any())
        {
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.Forge)) return "06_ForgeIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.NeoForge)) return "07_NeoForgeIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.Fabric)) return "05_FabricIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.Quilt)) return "09_QuiltIcon.png";
            if (e.ModLoaders.Any(a => a.Type == ModLoaderType.OptiFine)) return "08_OptiFineIcon.png";
        }

        return MinecraftEntry.Version.Type switch
        {
            MinecraftVersionType.Snapshot => "02_crafting_table_front.png",
            _ => "01_grass_block_side.png"
        };
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

    public class IconSizeProxy(MinecraftInstance instance)
    {
        public Bitmap this[int width] => instance.GetInstanceIcon(width);
    }
}

public partial class MinecraftInstanceConfig : ObservableObject
{
    [ObservableProperty] public partial string Note { get; set; }
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    [ObservableProperty] public partial DateTime LastPlayTime { get; set; } = DateTime.MinValue;
    [ObservableProperty] public partial Dictionary<string, bool> RecentPlayFavorites { get; set; } = [];
    [ObservableProperty] public partial bool EnableOverrideAdvancedOptions { get; set; }
    [ObservableProperty] public partial PortalVisibleMode PortalVisibleMode { get; set; } = PortalVisibleMode.NoOperation;

    [ObservableProperty] public partial Dictionary<string, long> PlayTimeByDate { get; set; } = [];

    public long ArchivedPlayTimeSeconds { get; set; }

    [JsonPropertyName("PlayTimeSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long LegacyPlayTimeSeconds { get; set; }

    [ObservableProperty] public partial int PlaySessions { get; set; }
}

public partial class JavaInstanceConfig : MinecraftInstanceConfig
{
    [ObservableProperty] public partial bool EnableIndependentInstance { get; set; } = true;
    [ObservableProperty] public partial bool EnableSpecificJava { get; set; }
    [ObservableProperty] public partial bool EnableOverrideMaxMemory { get; set; }
    [ObservableProperty] public partial int MinecraftMaxMemory { get; set; }
    [ObservableProperty] public partial string? JvmArgs { get; set; }
    [ObservableProperty] public partial JavaRuntimeEntry? SpecificJavaEntry { get; set; }
    [ObservableProperty] public partial GraphicsApi GraphicsBackend { get; set; } = GraphicsApi.Default;
    [ObservableProperty] public partial string? OpenGlRenderer { get; set; }
    [ObservableProperty] public partial string? VulkanRenderer { get; set; }
    [ObservableProperty] public partial bool EnableFullscreen { get; set; }
    [ObservableProperty] public partial bool AutoSetChineseLanguage { get; set; } = true;
    [ObservableProperty] public partial bool EnableGameOverlay { get; set; } = true;
    [ObservableProperty] public partial string? OverrideMinecraftWindowTitle { get; set; }
    [ObservableProperty] public partial int MinecraftWindowWidth { get; set; } = 854;
    [ObservableProperty] public partial int MinecraftWindowHeight { get; set; } = 480;
    [ObservableProperty] public partial string? BeforeLaunchCommand { get; set; }
    [ObservableProperty] public partial string? AfterLaunchCommand { get; set; }
    [ObservableProperty] public partial string? PackagedCommand { get; set; }
}

public enum MinecraftInstanceType
{
    Java,
    Bedrock
}