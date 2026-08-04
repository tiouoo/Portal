using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Module.AggregatedSearch;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("存储空间", "设置/存储空间", "Storage")]
public partial class Storage : UserControl
{
    public StorageViewModel ViewModel { get; }

    public Storage()
    {
        InitializeComponent();
        ViewModel = new StorageViewModel();
        DataContext = ViewModel;
    }

    public void TriggerRefresh()
    {
        _ = ViewModel.RefreshStorageDataAsync();
    }

}

public partial class StorageViewModel : ObservableObject
{
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

    private readonly string _portalDataPath = ConfigPath.UserDataRootPath;
    private readonly string _cachePath = ConfigPath.CacheFolderPath;
    private readonly string _bedrockDataPath = ConfigPath.BedrockDataRootPath;
    private readonly string _javaRuntimesPath = ConfigPath.JavaRuntimesPath;

    public StorageViewModel()
    {
        _ = RefreshStorageDataAsync();
    }

    [RelayCommand]
    public async Task RefreshStorageDataAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        string portalPath = _portalDataPath;
        PortalBytesRaw = 0;
        GameBytesRaw = 0;
        TotalBytesRaw = 0;
        var folders = Data.ConfigEntry.MinecraftFolders.ToList();
        Logger.Info($"[Storage] Refreshing storage usage for {folders.Count} Minecraft folder(s), Portal data at {portalPath}.");

        PortalFolders.Clear();
        PortalFolders.Add(new GameFolderStorageItem(
            "Portal 数据文件夹",
            "Portal 用户数据，删除将丢失所有账户信息、游戏设置等。",
            portalPath,
            0));
        PortalFolders.Add(new GameFolderStorageItem(
            "Portal 缓存文件夹",
            "Portal 下载与运行产生的缓存数据，删除后会在需要时重新生成。",
            _cachePath,
            0));
        PortalFolders.Add(new GameFolderStorageItem(
            "运行时文件夹",
            "Portal 管理的 Java 运行时，不计入游戏或 Portal 分类。",
            _javaRuntimesPath,
            0));
        
        GameFolders.Clear();

        if (OperatingSystem.IsWindows())
        {
            GameFolders.Add(new GameFolderStorageItem(
                "基岩版数据共享文件夹",
                "基岩版实例共享的游戏数据，包括世界、资源包和行为包等。",
                _bedrockDataPath,
                0));
        }
        
        foreach (var folder in folders)
        {
            GameFolders.Add(new GameFolderStorageItem(folder.FolderName, folder.FolderPath, folder.FolderPath, 0));
        }

        await Task.Run(() =>
        {
            try
            {
                long dataBytes = GetDirectorySize(portalPath, [_cachePath, _bedrockDataPath, _javaRuntimesPath]);
                long cacheBytes = GetDirectorySize(_cachePath);
                long portalBytes = dataBytes + cacheBytes;
                long javaBytes = GetDirectorySize(_javaRuntimesPath);

                long totalGameBytes = 0;
                var gameSizes = new List<(string FolderPath, long Size)>();
                foreach (var folder in folders)
                {
                    long size = GetDirectorySize(folder.FolderPath);
                    totalGameBytes += size;
                    gameSizes.Add((folder.FolderPath, size));
                }

                if (OperatingSystem.IsWindows())
                {
                    long bedrockBytes = GetDirectorySize(_bedrockDataPath);
                    totalGameBytes += bedrockBytes;
                    gameSizes.Add((_bedrockDataPath, bedrockBytes));
                }

                var gameBytes = totalGameBytes;
                // 后台线程只负责计算，绑定属性的赋值必须切回 UI 线程
                Dispatcher.UIThread.Post(() =>
                {
                    PortalBytesRaw = portalBytes;
                    PortalFolders[0].SizeBytes = dataBytes;
                    PortalFolders[1].SizeBytes = cacheBytes;
                    PortalFolders[2].SizeBytes = javaBytes;
                    foreach (var (folderPath, size) in gameSizes)
                    {
                        var item = GameFolders.FirstOrDefault(x => x.FolderPath == folderPath);
                        if (item != null)
                            item.SizeBytes = size;
                    }
                    GameBytesRaw = gameBytes;
                    TotalBytesRaw = portalBytes + gameBytes + javaBytes;
                    Logger.Info($"[Storage] Storage usage refreshed in {stopwatch.Elapsed}: Portal={portalBytes} bytes, game={gameBytes} bytes.");
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        });
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
                .Select(x => x.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
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
    public string FolderName { get; }
    public string Description { get; }
    public string FolderPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeString))]
    public partial long SizeBytes { get; set; }

    public string SizeString => SizeBytes.ToHumanReadableSize(1);

    public GameFolderStorageItem(string folderName, string description, string folderPath, long sizeBytes)
    {
        FolderName = folderName;
        Description = description;
        FolderPath = folderPath;
        SizeBytes = sizeBytes;
    }

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
