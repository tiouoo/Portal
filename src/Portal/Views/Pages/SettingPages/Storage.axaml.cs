using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.SettingPages;

public partial class StorageChartItem : ObservableObject
{
    public StorageChartItem(string id, string label, long sizeBytes, string color)
    {
        Id = id;
        Label = label;
        SizeBytes = sizeBytes;
        Color = color;
    }

    public string Id { get; }
    public string Label { get; }
    public string Color { get; }

    [ObservableProperty]
    public partial long SizeBytes { get; set; }
}

[AggregatedSearchPage("pages_storage", "pages_storagePath", "Storage")]
public partial class Storage : UserControl
{
    public Storage()
    {
        InitializeComponent();
        ViewModel = new StorageViewModel();
        DataContext = ViewModel;
    }

    public StorageViewModel ViewModel { get; }

    public void TriggerRefresh()
    {
        _ = ViewModel.RefreshStorageDataAsync();
    }
}

public partial class StorageViewModel : ObservableObject
{
    private readonly string _bedrockDataPath = ConfigPath.BedrockDataRootPath;
    private readonly string _cachePath = ConfigPath.CacheFolderPath;
    private readonly string _javaRuntimesPath = ConfigPath.JavaRuntimesPath;

    private readonly string _portalDataPath = ConfigPath.UserDataRootPath;
    private readonly string _multiplayerPath = Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer");
    private readonly string _updatePath = ConfigPath.UpdateFolderPath;

    public StorageViewModel()
    {
        _ = RefreshStorageDataAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PortalSizeString))]
    [NotifyPropertyChangedFor(nameof(TotalSizeString))]
    public partial double PortalBytesRaw { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameSizeString))]
    [NotifyPropertyChangedFor(nameof(TotalSizeString))]
    public partial double GameBytesRaw { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSizeString))]
    public partial double TotalBytesRaw { get; set; }

    public string TotalSizeString => TotalBytesRaw.ToHumanReadableSize(1);
    public string PortalSizeString => PortalBytesRaw.ToHumanReadableSize(1);
    public string GameSizeString => GameBytesRaw.ToHumanReadableSize(1);

    public ObservableCollection<GameFolderStorageItem> GameFolders { get; } = [];
    public ObservableCollection<GameFolderStorageItem> PortalFolders { get; } = [];
    public ObservableCollection<StorageChartItem> ChartItems { get; } = [];

    [RelayCommand]
    public async Task RefreshStorageDataAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var portalPath = _portalDataPath;
        PortalBytesRaw = 0;
        GameBytesRaw = 0;
        TotalBytesRaw = 0;
        var folders = Data.ConfigEntry.MinecraftFolders.ToList();
        Logger.Info(
            $"[Storage] Refreshing storage usage for {folders.Count} Minecraft folder(s), Portal data at {portalPath}.");

        PortalFolders.Clear();
        AddFolder(PortalFolders, SettingsLanguageManager.Instance.storage_portalData.CurrentValue(), portalPath,
            portalPath);

        GameFolders.Clear();

        foreach (var folder in folders)
            AddFolder(GameFolders, folder.FolderName, folder.FolderPath, folder.FolderPath);

        ChartItems.Clear();
        ChartItems.Add(new StorageChartItem("portal", SettingsLanguageManager.Instance.storage_portalData.CurrentValue(), 0, "#10b981"));
        ChartItems.Add(new StorageChartItem("cache", SettingsLanguageManager.Instance.storage_cache.CurrentValue(), 0, "#f59e0b"));
        ChartItems.Add(new StorageChartItem("java", SettingsLanguageManager.Instance.storage_minecraftJava.CurrentValue(), 0, "#3b82f6"));
        ChartItems.Add(new StorageChartItem("bedrock", SettingsLanguageManager.Instance.storage_minecraftBedrock.CurrentValue(), 0, "#06b6d4"));
        ChartItems.Add(new StorageChartItem("runtime", SettingsLanguageManager.Instance.storage_runtime.CurrentValue(), 0, "#8b5cf6"));
        ChartItems.Add(new StorageChartItem("other", SettingsLanguageManager.Instance.storage_other.CurrentValue(), 0, "#6b7280"));

        await Task.Run(() =>
        {
            try
            {
                var dataBytes = GetDirectorySize(portalPath,
                    [_cachePath, _updatePath, _bedrockDataPath, _javaRuntimesPath, _multiplayerPath]);
                var cacheBytes = GetDirectorySize(_cachePath);
                cacheBytes += GetDirectorySize(_updatePath);
                var javaBytes = GetDirectorySize(_javaRuntimesPath);
                var otherBytes = GetDirectorySize(_multiplayerPath);
                var portalBedrockBytes = GetDirectorySize(_bedrockDataPath);
                var portalRootBytes = dataBytes + cacheBytes + javaBytes + otherBytes + portalBedrockBytes;

                long totalGameBytes = 0;
                var gameSizes = new List<(string FolderPath, long Size)>();
                foreach (var folder in folders)
                {
                    var size = GetDirectorySize(folder.FolderPath);
                    totalGameBytes += size;
                    gameSizes.Add((folder.FolderPath, size));
                }

                var gameBytes = totalGameBytes;
                var externalBedrockBytes = folders.Sum(folder =>
                {
                    var root = folder.DetectedLayout.RootPath;
                    var name = folder.DetectedLayout.Kind == MinecraftFolderKind.PortalMc
                        ? "bedrock_instances"
                        : "bedrock_versions";
                    return GetDirectorySize(Path.Combine(root, name));
                });

                Dispatcher.UIThread.Post(() =>
                {
                    var portalBytes = dataBytes;
                    PortalBytesRaw = portalRootBytes;
                    PortalFolders[0].SizeBytes = portalRootBytes;
                    foreach (var (folderPath, size) in gameSizes)
                    {
                        var item = GameFolders.FirstOrDefault(x => x.FolderPath == folderPath);
                        if (item != null)
                            item.SizeBytes = size;
                    }

                    var javaGameBytes = Math.Max(0, gameBytes - externalBedrockBytes);
                    var bedrockGameBytes = portalBedrockBytes + externalBedrockBytes;
                    GameBytesRaw = javaGameBytes + bedrockGameBytes;
                    TotalBytesRaw = PortalBytesRaw + gameBytes;
                    SetChartSize("portal", dataBytes);
                    SetChartSize("cache", cacheBytes);
                    SetChartSize("runtime", javaBytes);
                    SetChartSize("java", javaGameBytes);
                    SetChartSize("bedrock", bedrockGameBytes);
                    SetChartSize("other", otherBytes);
                    Logger.Info(
                        $"[Storage] Storage usage refreshed in {stopwatch.Elapsed}: Portal={portalBytes} bytes, game={gameBytes} bytes.");
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        });
    }

    private static void AddFolder(ICollection<GameFolderStorageItem> target, string name, string path, string description)
    {
        target.Add(new GameFolderStorageItem(name, description, path, 0));
    }

    private void SetChartSize(string id, long size)
    {
        var item = ChartItems.FirstOrDefault(x => x.Id == id);
        if (item != null) item.SizeBytes = size;
    }

    private long GetDirectorySize(string path, IEnumerable<string>? excludedDirectories = null)
    {
        if (!Directory.Exists(path)) return 0;

        long totalBytes = 0;

        try
        {
            var di = new DirectoryInfo(path);

            var excludedPaths = excludedDirectories?
                .Select(Path.GetFullPath)
                .Select(x =>
                    x.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar)
                .ToArray() ?? [];

            Parallel.ForEach(di.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(file => !excludedPaths.Any(excludedPath => file.FullName.StartsWith(excludedPath,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))),
                () => 0L,
                (fileInfo, loopState, localState) =>
                {
                    try
                    {
                        localState += fileInfo.Length;
                    }
                    catch (FileNotFoundException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                    return localState;
                },
                localResult => { Interlocked.Add(ref totalBytes, localResult); });
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }

        return totalBytes;
    }
}

public partial class GameFolderStorageItem : ObservableObject
{
    public GameFolderStorageItem(string folderName, string description, string folderPath, long sizeBytes)
    {
        FolderName = folderName;
        Description = description;
        FolderPath = folderPath;
        SizeBytes = sizeBytes;
    }

    public string FolderName { get; }
    public string Description { get; }
    public string FolderPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeString))]
    public partial long SizeBytes { get; set; }

    public string SizeString => SizeBytes.ToHumanReadableSize(1);

    [RelayCommand]
    public void OpenFolder(object? parameter)
    {
        if (parameter is Control control)
        {
            Logger.Info($"[Storage] Opening storage folder {FolderPath}.");
            _ = control.GetTopLevel().Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(FolderPath));
        }
    }
}

public class ByteSizeDisplayer : NumberDisplayer<double>
{
    protected override Type StyleKeyOverride { get; } = typeof(NumberDisplayerBase);

    protected override InterpolatingAnimator<double> GetAnimator()
    {
        return new DoubleAnimator();
    }

    protected override string GetString(double value)
    {
        return value.ToHumanReadableSize(1);
    }

    private class DoubleAnimator : InterpolatingAnimator<double>
    {
        public override double Interpolate(double progress, double oldValue, double newValue)
        {
            return oldValue + (newValue - oldValue) * progress;
        }
    }
}
