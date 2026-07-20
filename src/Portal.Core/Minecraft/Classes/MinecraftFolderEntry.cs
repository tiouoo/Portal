using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Components.Parser;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common.Extensions;

namespace Portal.Core.Minecraft.Classes;

public partial class MinecraftFolderEntry : ObservableObject, IEquatable<MinecraftFolderEntry>
{
    [ObservableProperty] public partial string FolderName { get; set; }
    [ObservableProperty] public partial string FolderPath { get; set; }
    [ObservableProperty] public partial MinecraftFolderKind FolderKind { get; set; } = MinecraftFolderKind.Auto;

    public MinecraftFolderLayout DetectedLayout
    {
        get
        {
            var detected = MinecraftFolderLayout.Detect(FolderPath);
            return detected.Kind == MinecraftFolderKind.Unknown && FolderKind == MinecraftFolderKind.Standard
                ? new MinecraftFolderLayout(MinecraftFolderKind.Standard, FolderPath, FolderPath,
                    "传统 .minecraft 文件夹")
                : detected;
        }
    }
    public string FolderTypeDescription => DetectedLayout.DisplayName;
    public bool SupportsTraditionalInstallation => DetectedLayout.SupportsTraditionalInstallation;

    private int _instanceCount;
    private string _folderSize = "0.0";
    private string _sizeUnit = "B";
    private bool _isRefreshing;

    public string FolderSize
    {
        get
        {
            _ = RefreshDataAsync();
            return _folderSize;
        }
        private set => SetProperty(ref _folderSize, value);
    }

    public string SizeUnit
    {
        get
        {
            _ = RefreshDataAsync();
            return _sizeUnit;
        }
        private set => SetProperty(ref _sizeUnit, value);
    }

    public int InstanceCount
    {
        get
        {
            _ = RefreshDataAsync();
            return _instanceCount;
        }
        private set => SetProperty(ref _instanceCount, value);
    }

    public MinecraftFolderEntry()
    {
        PropertyChanged += (_, e) => 
        { 
            if (e.PropertyName is nameof(FolderPath) or nameof(FolderName) or nameof(FolderKind))
            {
                if (e.PropertyName == nameof(FolderPath))
                {
                    OnPropertyChanged(nameof(DetectedLayout));
                    OnPropertyChanged(nameof(FolderTypeDescription));
                    OnPropertyChanged(nameof(SupportsTraditionalInstallation));
                }
                Events.RaiseCoreSaveSettings(); 
            }
        };
    }

    private async Task RefreshDataAsync()
    {
        if (_isRefreshing || string.IsNullOrEmpty(FolderPath) || !Directory.Exists(FolderPath)) 
            return;

        _isRefreshing = true;

        try
        {
            int freshCount = await Task.Run(() =>
            {
                try
                {
                    var layout = DetectedLayout;
                    return layout.Kind switch
                    {
                        MinecraftFolderKind.Standard => new MinecraftParser(layout.RootPath).GetMinecrafts().Count +
                                                       CountBedrockInstances(layout.RootPath),
                        MinecraftFolderKind.ModrinthApp => CountDirectories(Path.Combine(layout.RootPath, "profiles")),
                        MinecraftFolderKind.ModrinthProfile => 1,
                        MinecraftFolderKind.MultiMc => CountInstances(Path.Combine(layout.RootPath, "instances"),
                            "mmc-pack.json"),
                        MinecraftFolderKind.MultiMcInstance => 1,
                        MinecraftFolderKind.BakaXl => CountInstances(Path.Combine(layout.RootPath, "instances"),
                            "package.info"),
                        MinecraftFolderKind.BakaXlInstance => 1,
                        MinecraftFolderKind.CurseForge => CountInstances(Path.Combine(layout.RootPath, "Instances"),
                            "minecraftinstance.json"),
                        MinecraftFolderKind.CurseForgeInstance => 1,
                        _ => 0
                    };
                }
                catch
                {
                    return 0;
                }
            });

            var totalBytes = await Task.Run(() =>
            {
                try
                {
                    var di = new DirectoryInfo(FolderPath);
                    return di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
                }
                catch
                {
                    return 0L;
                }
            });

            var a = ((double)totalBytes).GetReadableRaw(1);

            InstanceCount = freshCount;
            FolderSize = a.Value.ToString("F1");
            SizeUnit = a.Unit;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static int CountDirectories(string path) => Directory.Exists(path)
        ? Directory.GetDirectories(path).Length
        : 0;

    private static int CountInstances(string path, string marker) => Directory.Exists(path)
        ? Directory.GetDirectories(path).Count(directory => File.Exists(Path.Combine(directory, marker)))
        : 0;

    private static int CountBedrockInstances(string rootPath) => CountInstances(
        Path.Combine(rootPath, "bedrock_versions"), "appxmanifest.xml");

    public void OpenFolder(object parameter)
    {
        var topLevel = (parameter as Control)?.GetTopLevel();
        topLevel?.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(FolderPath));
    }

    public bool Equals(MinecraftFolderEntry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(FolderPath, other.FolderPath, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MinecraftFolderEntry);
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(FolderPath);
    }

    public static bool operator ==(MinecraftFolderEntry? left, MinecraftFolderEntry? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(MinecraftFolderEntry? left, MinecraftFolderEntry? right)
    {
        return !(left == right);
    }
}
