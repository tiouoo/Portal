using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.Core.Minecraft.Classes;

/// <summary>
/// Lazily calculated storage usage for one Minecraft instance.
/// </summary>
public partial class InstanceStorageUsage : ObservableObject
{
    private readonly MinecraftInstance _instance;
    private Task? _loadTask;
    private readonly object _loadLock = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionFolderSizeText), nameof(ModsPercentageText),
        nameof(ResourcePacksPercentageText), nameof(ShaderPacksPercentageText), nameof(SavesPercentageText),
        nameof(ScreenshotsPercentageText), nameof(LogsPercentageText), nameof(CrashReportsPercentageText),
        nameof(OtherPercentageText), nameof(ModsDisplayText), nameof(ResourcePacksDisplayText),
        nameof(ShaderPacksDisplayText), nameof(SavesDisplayText))]
    private long _versionFolderBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModsSizeText), nameof(ModsPercentageText), nameof(ModsDisplayText))]
    private long _modsBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResourcePacksSizeText), nameof(ResourcePacksPercentageText),
        nameof(ResourcePacksDisplayText))]
    private long _resourcePacksBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShaderPacksSizeText), nameof(ShaderPacksPercentageText),
        nameof(ShaderPacksDisplayText))]
    private long _shaderPacksBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavesSizeText), nameof(SavesPercentageText), nameof(SavesDisplayText))]
    private long _savesBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ScreenshotsSizeText), nameof(ScreenshotsPercentageText))]
    private long _screenshotsBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ConfigSizeText), nameof(ConfigPercentageText))]
    private long _configBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LogsSizeText), nameof(LogsPercentageText))]
    private long _logsBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CrashReportsSizeText), nameof(CrashReportsPercentageText))]
    private long _crashReportsBytes;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OtherSizeText), nameof(OtherPercentageText))]
    private long _otherBytes;

    public bool CanDisplayPercentage => _instance.Config.EnableIndependentInstance;
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

    public InstanceStorageUsage(MinecraftInstance instance)
    {
        _instance = instance;
    }

    public Task EnsureLoadedAsync()
    {
        lock (_loadLock)
            return _loadTask ??= LoadAsync();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(CanDisplayPercentage));
        OnPropertyChanged(nameof(ModsDisplayText));
        OnPropertyChanged(nameof(ResourcePacksDisplayText));
        OnPropertyChanged(nameof(ShaderPacksDisplayText));
        OnPropertyChanged(nameof(SavesDisplayText));
        lock (_loadLock)
            _loadTask = LoadAsync();
    }

    private async Task LoadAsync()
    {
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
            var otherBytes = _instance.Config.EnableIndependentInstance
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

    private string FormatPercentage(long bytes) => VersionFolderBytes == 0
        ? "0%"
        : $"{bytes * 100d / VersionFolderBytes:F1}%";

    private string FormatSizeAndPercentage(long bytes) => CanDisplayPercentage
        ? $"{FormatSize(bytes)} / {FormatPercentage(bytes)}"
        : FormatSize(bytes);

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
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // Ignore files changed or removed while scanning.
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore files the current user cannot inspect.
                }
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
}