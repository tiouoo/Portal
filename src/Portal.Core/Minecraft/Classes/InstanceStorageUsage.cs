using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Instance.Bedrock;

namespace Portal.Core.Minecraft.Classes;

public partial class InstanceStorageUsage : ObservableObject
{
    private readonly MinecraftInstance _instance;
    private readonly object _loadLock = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BehaviorPacksSizeText), nameof(ResourceContentSizeText))]
    private long _behaviorPacksBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ConfigSizeText), nameof(ConfigPercentageText))]
    private long _configBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CrashReportsSizeText), nameof(CrashReportsPercentageText),
        nameof(CrashReportsDisplayText))]
    private long _crashReportsBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DevelopmentPacksSizeText), nameof(ResourceContentSizeText))]
    private long _developmentPacksBytes;

    private Task? _loadTask;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LogsSizeText), nameof(LogsPercentageText))]
    private long _logsBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModsSizeText), nameof(ModsPercentageText), nameof(ModsDisplayText))]
    private long _modsBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OtherSizeText), nameof(OtherPercentageText))]
    private long _otherBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResourcePacksSizeText), nameof(ResourcePacksPercentageText),
        nameof(ResourcePacksDisplayText), nameof(ResourceContentSizeText))]
    private long _resourcePacksBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavesSizeText), nameof(SavesPercentageText), nameof(SavesDisplayText))]
    private long _savesBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ScreenshotsSizeText), nameof(ScreenshotsPercentageText))]
    private long _screenshotsBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShaderPacksSizeText), nameof(ShaderPacksPercentageText),
        nameof(ShaderPacksDisplayText))]
    private long _shaderPacksBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SkinPacksSizeText), nameof(ResourceContentSizeText))]
    private long _skinPacksBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionFolderSizeText), nameof(ModsPercentageText),
        nameof(ResourcePacksPercentageText), nameof(ShaderPacksPercentageText), nameof(SavesPercentageText),
        nameof(ScreenshotsPercentageText), nameof(LogsPercentageText),
        nameof(OtherPercentageText), nameof(ModsDisplayText), nameof(ResourcePacksDisplayText),
        nameof(ShaderPacksDisplayText), nameof(SavesDisplayText))]
    private long _versionFolderBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(WorldTemplatesSizeText))]
    private long _worldTemplatesBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(WorldsSizeText))]
    private long _worldsBytes;

    public InstanceStorageUsage(MinecraftInstance instance)
    {
        _instance = instance;
    }

    public bool CanDisplayPercentage => _instance.JavaConfig?.EnableIndependentInstance == true;
    public string VersionFolderSizeText => FormatSize(VersionFolderBytes);
    public string ModsSizeText => FormatSize(ModsBytes);
    public string ResourcePacksSizeText => FormatSize(ResourcePacksBytes);
    public string ShaderPacksSizeText => FormatSize(ShaderPacksBytes);
    public string SavesSizeText => FormatSize(SavesBytes);
    public string ScreenshotsSizeText => FormatSize(ScreenshotsBytes);
    public string ConfigSizeText => FormatSize(ConfigBytes);
    public string LogsSizeText => FormatSize(LogsBytes);
    public string CrashReportsSizeText => FormatSize(CrashReportsBytes);
    public string OtherSizeText => FormatSize(OtherBytes);
    public string WorldsSizeText => FormatSize(WorldsBytes);
    public string BehaviorPacksSizeText => FormatSize(BehaviorPacksBytes);
    public string SkinPacksSizeText => FormatSize(SkinPacksBytes);
    public string WorldTemplatesSizeText => FormatSize(WorldTemplatesBytes);
    public string DevelopmentPacksSizeText => FormatSize(DevelopmentPacksBytes);
    public string ResourceContentSizeText => FormatSize(ResourcePacksBytes + BehaviorPacksBytes + SkinPacksBytes);
    public string ModsPercentageText => FormatPercentage(ModsBytes);
    public string ResourcePacksPercentageText => FormatPercentage(ResourcePacksBytes);
    public string ShaderPacksPercentageText => FormatPercentage(ShaderPacksBytes);
    public string SavesPercentageText => FormatPercentage(SavesBytes);
    public string ScreenshotsPercentageText => FormatPercentage(ScreenshotsBytes);
    public string ConfigPercentageText => FormatPercentage(ConfigBytes);
    public string LogsPercentageText => FormatPercentage(LogsBytes);
    public string CrashReportsPercentageText => FormatPercentage(CrashReportsBytes);
    public string OtherPercentageText => FormatPercentage(OtherBytes);
    public string ModsDisplayText => FormatSizeAndPercentage(ModsBytes);
    public string ResourcePacksDisplayText => FormatSizeAndPercentage(ResourcePacksBytes);
    public string ShaderPacksDisplayText => FormatSizeAndPercentage(ShaderPacksBytes);
    public string SavesDisplayText => FormatSizeAndPercentage(SavesBytes);
    public string ScreenshotsDisplayText => FormatSizeAndPercentage(ScreenshotsBytes);
    public string ConfigDisplayText => FormatSizeAndPercentage(ConfigBytes);
    public string LogsDisplayText => FormatSizeAndPercentage(LogsBytes);
    public string CrashReportsDisplayText => FormatSizeAndPercentage(CrashReportsBytes);
    public string OtherDisplayText => FormatSizeAndPercentage(OtherBytes);

    public Task EnsureLoadedAsync()
    {
        lock (_loadLock)
        {
            return _loadTask ??= LoadAsync();
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(CanDisplayPercentage));
        OnPropertyChanged(nameof(ModsDisplayText));
        OnPropertyChanged(nameof(ResourcePacksDisplayText));
        OnPropertyChanged(nameof(ShaderPacksDisplayText));
        OnPropertyChanged(nameof(SavesDisplayText));
        lock (_loadLock)
        {
            _loadTask = LoadAsync();
        }
    }

    public Task RefreshBedrockWorldsAsync()
    {
        if (_instance.BedrockConfig is not { } config)
            return Task.CompletedTask;

        return RefreshBedrockWorldsAsync(config);
    }

    private async Task RefreshBedrockWorldsAsync(BedrockInstanceConfig config)
    {
        WorldsBytes = await Task.Run(() => GetBedrockWorldsSize(config));
    }

    private async Task LoadAsync()
    {
        if (_instance.IsBedrock)
        {
            var bedrockUsage = await Task.Run(() =>
            {
                var instanceBytes = GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder));
                var worlds = _instance.BedrockConfig is { } config ? GetBedrockWorldsSize(config) : 0;
                var resources =
                    GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.ResourcePacksFolder));
                var behaviors =
                    GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.BehaviorPacksFolder));
                var skins = GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.SkinPacksFolder));
                var templates =
                    GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.WorldTemplatesFolder));
                var development =
                    GetDirectorySize(
                        _instance.GetSpecialFolder(MinecraftSpecialFolder.DevelopmentResourcePacksFolder)) +
                    GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.DevelopmentBehaviorPacksFolder));
                var screenshots =
                    GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.ScreenshotsFolder));
                return (instanceBytes, worlds, resources, behaviors, skins, templates, development, screenshots);
            });

            VersionFolderBytes = bedrockUsage.instanceBytes;
            WorldsBytes = bedrockUsage.worlds;
            ResourcePacksBytes = bedrockUsage.resources;
            BehaviorPacksBytes = bedrockUsage.behaviors;
            SkinPacksBytes = bedrockUsage.skins;
            WorldTemplatesBytes = bedrockUsage.templates;
            DevelopmentPacksBytes = bedrockUsage.development;
            ScreenshotsBytes = bedrockUsage.screenshots;
            return;
        }

        var usage = await Task.Run(() =>
        {
            var versionBytes = GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.InstanceFolder));
            var modsBytes = GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder));
            var resourcePacksBytes =
                GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.ResourcePacksFolder));
            var shaderPacksBytes =
                GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder));
            var savesBytes = GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.SavesFolder));
            var screenshotsBytes =
                GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.ScreenshotsFolder));
            var configBytes = GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.ConfigFolder));
            var logsBytes = GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.LogsFolder));
            var crashReportsBytes =
                GetDirectorySize(_instance.GetSpecialFolder(MinecraftSpecialFolder.CrashReportsFolder));


            var categorizedBytes = modsBytes + resourcePacksBytes + shaderPacksBytes + savesBytes +
                                   screenshotsBytes + configBytes + logsBytes + crashReportsBytes;
            var otherBytes = _instance.JavaConfig?.EnableIndependentInstance == true
                ? Math.Max(0, versionBytes - categorizedBytes)
                : versionBytes;

            return (versionBytes, modsBytes, resourcePacksBytes, shaderPacksBytes, savesBytes,
                screenshotsBytes, configBytes, logsBytes, crashReportsBytes, otherBytes);
        });

        VersionFolderBytes = usage.versionBytes;
        ModsBytes = usage.modsBytes;
        ResourcePacksBytes = usage.resourcePacksBytes;
        ShaderPacksBytes = usage.shaderPacksBytes;
        SavesBytes = usage.savesBytes;
        ScreenshotsBytes = usage.screenshotsBytes;
        ConfigBytes = usage.configBytes;
        LogsBytes = usage.logsBytes;
        CrashReportsBytes = usage.crashReportsBytes;
        OtherBytes = usage.otherBytes;
    }

    private string FormatPercentage(long bytes)
    {
        return VersionFolderBytes == 0
            ? "0%"
            : $"{bytes * 100d / VersionFolderBytes:F1}%";
    }

    private string FormatSizeAndPercentage(long bytes)
    {
        return CanDisplayPercentage
            ? $"{FormatSize(bytes)} / {FormatPercentage(bytes)}"
            : FormatSize(bytes);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:F0} {units[unit]}" : $"{value:F1} {units[unit]}";
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = true,
                         AttributesToSkip = FileAttributes.ReparsePoint
                     }))
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return total;
    }

    private static long GetBedrockWorldsSize(BedrockInstanceConfig config)
    {
        return BedrockDataPathResolver.GetWorldUserIds(config)
            .Sum(userId => GetDirectorySize(BedrockDataPathResolver.GetWorldsFolder(config, userId)));
    }
}