using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.SettingPages;

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

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        _ = (sender as Control)!.GetTopLevel().Launcher
            .LaunchDirectoryInfoAsync(new DirectoryInfo(ConfigPath.UserDataRootPath));
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

    private readonly string _portalDataPath = ConfigPath.UserDataRootPath;

    public StorageViewModel()
    {
        _ = RefreshStorageDataAsync();
    }

    [RelayCommand]
    public async Task RefreshStorageDataAsync()
    {
        string portalPath = _portalDataPath;
        PortalBytesRaw = 0;
        GameBytesRaw = 0;
        TotalBytesRaw = 0;
        var folders = Data.ConfigEntry.MinecraftFolders.ToList();

        GameFolders.Clear();
        foreach (var folder in folders)
        {
            GameFolders.Add(new GameFolderStorageItem(folder.FolderName, folder.FolderPath, 0));
        }

        await Task.Run(() =>
        {
            try
            {
                long portalBytes = GetDirectorySize(portalPath);
                PortalBytesRaw = portalBytes;

                long totalGameBytes = 0;
                foreach (var folder in folders)
                {
                    long size = GetDirectorySize(folder.FolderPath);
                    totalGameBytes += size;

                    var item = GameFolders.FirstOrDefault(x => x.FolderPath == folder.FolderPath);
                    if (item != null)
                    {
                        item.SizeBytes = size;
                    }
                }

                GameBytesRaw = totalGameBytes;
                TotalBytesRaw = portalBytes + totalGameBytes;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error: {ex.Message}");
            }
        });
    }

    private long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;

        long totalBytes = 0;

        try
        {
            var di = new DirectoryInfo(path);

            Parallel.ForEach(di.EnumerateFiles("*", SearchOption.AllDirectories),
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
            Logger.Error($"Directory walk failed: {ex.Message}");
        }

        return totalBytes;
    }
}

public partial class GameFolderStorageItem : ObservableObject
{
    public string FolderName { get; }
    public string FolderPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeString))]
    public partial long SizeBytes { get; set; }

    public string SizeString => SizeBytes.ToHumanReadableSize(1);

    public GameFolderStorageItem(string folderName, string folderPath, long sizeBytes)
    {
        FolderName = folderName;
        FolderPath = folderPath;
        SizeBytes = sizeBytes;
    }

    [RelayCommand]
    public void OpenFolder(object? parameter)
    {
        if (parameter is Control control)
        {
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