using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;

namespace Portal.Core.Operations.OpenFile;

public partial class NewMinecraftFolder : UserControl
{
    public NewMinecraftFolder()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not NewMinecraftFolderViewModel viewModel ||
            ImportLauncherDropDown.Flyout is not MenuFlyout menu)
            return;

        menu.Items.Clear();
        foreach (var launcher in viewModel.DetectedLaunchers)
        {
            var item = new MenuItem { Header = launcher.Name, Tag = launcher };
            item.Click += (_, _) => viewModel.Import(launcher);
            menu.Items.Add(item);
        }
    }
}

public partial class NewMinecraftFolderViewModel : ObservableObject, IDialogContext
{
    private readonly List<string> _paths;
    [ObservableProperty] public partial string? FolderName { get; set; }

    [ObservableProperty] public partial string? FolderPath { get; set; }
    [ObservableProperty] public partial bool Warning { get; set; }
    [ObservableProperty] public partial bool NoExist { get; set; }
    [ObservableProperty] public partial bool Contain { get; set; }
    [ObservableProperty] public partial string FolderTypeDescription { get; set; } = "请选择 Minecraft 文件夹";
    [ObservableProperty] public partial bool IsFolderRecognized { get; set; }
    public IReadOnlyList<DetectedLauncherFolder> DetectedLaunchers { get; }
    public bool HasImports => DetectedLaunchers.Count > 0;

    public ICommand NextCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand FolderPickedCommand { get; }

    private readonly Dictionary<string, List<string>> _errors = new();

    public NewMinecraftFolderViewModel(List<string> paths)
    {
        _paths = paths;
        DetectedLaunchers = FindInstalledLaunchers(paths);
        NextCommand = new RelayCommand(Next, CanNext);
        CancelCommand = new RelayCommand(Cancel);
        FolderPickedCommand = new RelayCommand<IReadOnlyList<IStorageItem>?>(OnFolderPicked);
    }

    partial void OnFolderPathChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(value.Trim()))
        {
            Warning = false;
            NoExist = true;
            Contain = false;
            FolderTypeDescription = "请选择一个存在的 Minecraft 文件夹";
            IsFolderRecognized = false;
            return;
        }

        var folderPath = value.Trim();
        var layout = MinecraftFolderLayout.Detect(folderPath);
        FolderName = new DirectoryInfo(layout.SelectedPath).Name;
        FolderTypeDescription = layout.DisplayName;
        IsFolderRecognized = layout.Kind != MinecraftFolderKind.Unknown;
        Contain = _paths.Contains(folderPath);
        Warning = layout.Kind == MinecraftFolderKind.Standard &&
                  !MinecraftFolderLayout.LooksLikeMinecraftRoot(folderPath);

        NoExist = false;

        ((RelayCommand)NextCommand).NotifyCanExecuteChanged();
    }

    partial void OnFolderNameChanged(string? value)
    {
        ((RelayCommand)NextCommand).NotifyCanExecuteChanged();
    }

    private void OnFolderPicked(IReadOnlyList<IStorageItem>? items)
    {
        if (items is not { Count: > 0 })
            return;

        var picked = items[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(picked))
            return;

        FolderPath = MinecraftFolderLayout.ResolveGameFolder(picked);
    }

    private bool CanNext()
    {
        return !string.IsNullOrWhiteSpace(FolderName)
               && !string.IsNullOrWhiteSpace(FolderPath)
               && !NoExist && !Contain;
    }

    private void Next()
    {
        RequestClose?.Invoke(this, new MinecraftFolderEntry()
        {
            FolderName = FolderName.Trim(),
            FolderPath = FolderPath.Trim(),
            FolderKind = MinecraftFolderLayout.Detect(FolderPath.Trim()).Kind
        });
    }

    public void Import(DetectedLauncherFolder launcher)
    {
        var layout = MinecraftFolderLayout.Detect(launcher.Path);
        RequestClose?.Invoke(this, new MinecraftFolderEntry
        {
            FolderName = launcher.Name,
            FolderPath = layout.SelectedPath,
            FolderKind = layout.Kind
        });
    }

    private static IReadOnlyList<DetectedLauncherFolder> FindInstalledLaunchers(IEnumerable<string> configuredPaths)
    {
        var existing = configuredPaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<DetectedLauncherFolder>();
        foreach (var (name, relativeParts) in GetKnownLaunchers())
        {
            var path = ResolveExistingDataPath(relativeParts);
            if (string.IsNullOrEmpty(path))
                continue;
            if (MinecraftFolderLayout.Detect(path).Kind == MinecraftFolderKind.Unknown)
                continue;
            if (existing.Contains(Path.GetFullPath(path)))
                continue;
            result.Add(new DetectedLauncherFolder(name, path));
        }

        return result;
    }

    private static (string Name, string[] RelativeParts)[] GetKnownLaunchers() => new[]
    {
        ("Modrinth App", new[] { "ModrinthApp" }),
        ("Axolotl", new[] { "red.ghs.axolotl", "profiles", "Vanilla 26.2" }),
        ("CurseForge App", new[] { "curseforge", "minecraft" }),
        ("BakaXL", new[] { ".BakaXL", "minecraft" })
    };

    /// <summary>
    /// 跨平台定位启动器数据目录：
    /// Windows 通常位于 %APPDATA%；macOS 位于 ~/Library/Application Support；
    /// Linux 下 Theseus（Modrinth / Axolotl）等使用 $XDG_DATA_HOME 或 ~/.local/share。
    /// 依次尝试各根目录，返回第一个存在的路径。
    /// </summary>
    private static string? ResolveExistingDataPath(string[] relativeParts)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            var path = Path.Combine(new[] { root }.Concat(relativeParts).ToArray());
            if (Directory.Exists(path))
                return path;
        }

        return null;
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;
}

public sealed record DetectedLauncherFolder(string Name, string Path);
