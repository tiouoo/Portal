using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
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
            var item = new MenuItem
            {
                Header = launcher.Name,
                Tag = launcher
            };


            item.Click += (_, _) => { viewModel.Import(launcher); };


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


    [ObservableProperty]
    public partial string FolderTypeDescription { get; set; }
        = "请选择 Minecraft 文件夹";


    [ObservableProperty] public partial bool IsFolderRecognized { get; set; }


    public IReadOnlyList<DetectedLauncherFolder> DetectedLaunchers { get; }


    public bool HasImports => DetectedLaunchers.Count > 0;


    public ICommand NextCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand FolderPickedCommand { get; }


    public NewMinecraftFolderViewModel(List<string> paths)
    {
        _paths = paths;


        DetectedLaunchers = FindInstalledLaunchers(paths);


        NextCommand = new RelayCommand(
            Next,
            CanNext);


        CancelCommand = new RelayCommand(
            Cancel);


        FolderPickedCommand =
            new RelayCommand<IReadOnlyList<IStorageItem>?>(
                OnFolderPicked);
    }


    partial void OnFolderPathChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Directory.Exists(value.Trim()))
        {
            Warning = false;
            NoExist = true;
            Contain = false;
            FolderTypeDescription =
                "请选择一个存在的 Minecraft 文件夹";
            IsFolderRecognized = false;

            return;
        }


        var folderPath = value.Trim();

        var layout = MinecraftFolderLayout.Detect(folderPath);

        // 空文件夹自动识别为 Portal MC 布局
        if (layout.Kind == MinecraftFolderKind.Standard && IsDirectoryEmpty(folderPath))
        {
            layout = MinecraftFolderLayout.FromFolderKind(MinecraftFolderKind.PortalMc, folderPath);
        }

        FolderName = new DirectoryInfo(layout.SelectedPath).Name;
        FolderTypeDescription = layout.DisplayName;
        IsFolderRecognized = layout.Kind != MinecraftFolderKind.Unknown;

        Contain = _paths.Contains(folderPath, StringComparer.OrdinalIgnoreCase);

        // 只有非空且非 Minecraft 结构的文件夹才显示警告
        Warning = layout.Kind == MinecraftFolderKind.Standard &&
                  !IsDirectoryEmpty(folderPath) &&
                  !MinecraftFolderLayout.LooksLikeMinecraftRoot(folderPath);

        NoExist = false;

        ((RelayCommand)NextCommand).NotifyCanExecuteChanged();
    }


    partial void OnFolderNameChanged(string? value)
    {
        ((RelayCommand)NextCommand)
            .NotifyCanExecuteChanged();
    }


    private void OnFolderPicked(IReadOnlyList<IStorageItem>? items)
    {
        if (items is not { Count: > 0 })
            return;


        var picked =
            items[0].TryGetLocalPath();


        if (string.IsNullOrWhiteSpace(picked))
            return;


        FolderPath =
            MinecraftFolderLayout.ResolveGameFolder(picked);
    }


    private bool CanNext()
    {
        return
            !string.IsNullOrWhiteSpace(FolderName)
            &&
            !string.IsNullOrWhiteSpace(FolderPath)
            &&
            !NoExist
            &&
            !Contain;
    }


    private void Next()
    {
        var folderPath = FolderPath!.Trim();
        var layout = MinecraftFolderLayout.Detect(folderPath);

        // 空文件夹自动初始化为 Portal MC 布局
        if (layout.Kind == MinecraftFolderKind.Standard && IsDirectoryEmpty(folderPath))
        {
            Directory.CreateDirectory(Path.Combine(folderPath, "meta"));
            Directory.CreateDirectory(Path.Combine(folderPath, "instances"));
            Directory.CreateDirectory(Path.Combine(folderPath, "bedrock_instances"));

            RequestClose?.Invoke(
                this,
                new MinecraftFolderEntry
                {
                    FolderName = FolderName!.Trim(),
                    FolderPath = folderPath,
                    FolderKind = MinecraftFolderKind.PortalMc
                });
            return;
        }

        RequestClose?.Invoke(
            this,
            new MinecraftFolderEntry
            {
                FolderName = FolderName!.Trim(),
                FolderPath = folderPath,
                FolderKind = layout.Kind
            });
    }

    private static bool IsDirectoryEmpty(string path)
    {
        try
        {
            if (Directory.GetDirectories(path).Length > 0)
                return false;

            foreach (var file in Directory.GetFiles(path))
            {
                try
                {
                    var attr = File.GetAttributes(file);
                    if ((attr & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }


    public void Import(DetectedLauncherFolder launcher)
    {
        RequestClose?.Invoke(
            this,
            new MinecraftFolderEntry
            {
                FolderName = launcher.Name,
                FolderPath = launcher.Path,
                FolderKind = launcher.Kind
            });
    }


    private static IReadOnlyList<DetectedLauncherFolder>
        FindInstalledLaunchers(IEnumerable<string> configuredPaths)
    {
        var existing =
            configuredPaths
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Path.GetFullPath)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);


        var result =
            new List<DetectedLauncherFolder>();


        foreach (var launcher in GetKnownLaunchers())
        {
            var path = launcher.Reader();


            if (string.IsNullOrWhiteSpace(path))
                continue;


            path = Path.GetFullPath(path);


            // 已经导入过的不显示
            if (existing.Contains(path))
                continue;


            if (MinecraftFolderLayout.Detect(path).Kind
                == MinecraftFolderKind.Unknown)
                continue;


            result.Add(
                new DetectedLauncherFolder(
                    launcher.Name,
                    path,
                    launcher.Kind));
        }


        return result;
    }


    private static MinecraftFolderProvider[] GetKnownLaunchers()
    {
        return
        [
            new(
                MinecraftFolderKind.Modrinth,
                "Modrinth",
                ReadModrinthFolder),

            new(
                MinecraftFolderKind.Modrinth,
                "Axolotl / Theseus",
                ReadAxolotlFolder),

            new(
                MinecraftFolderKind.CurseForge,
                "CurseForge",
                () =>
                {
                    var roots = new[]
                    {
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.ApplicationData),

                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile)
                    };


                    foreach(var root in roots)
                    {
                        var path = Path.Combine(
                            root,
                            "curseforge",
                            "minecraft");


                        if(Directory.Exists(path))
                            return path;
                    }


                    return null;
                }),

            new(
                MinecraftFolderKind.MultiMc,
                "MultiMC / Prism Launcher",
                () =>
                {
                    var roots = new[]
                    {
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.ApplicationData),

                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile)
                    };

                    foreach (var root in roots)
                    {
                        foreach (var launcherDir in new[] { "MultiMC", "PrismLauncher", ".BakaXL" })
                        {
                            var path = Path.Combine(root, launcherDir,
                                launcherDir == ".BakaXL" ? "minecraft" : string.Empty);
                            if (Directory.Exists(path))
                                return path;
                        }
                    }

                    return null;
                })
        ];
    }


    private static string? ReadModrinthFolder()
    {
        var db =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "ModrinthApp",
                "app.db");


        return ReadCustomDirectory(db);
    }


    private static string? ReadAxolotlFolder()
    {
        var db =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "red.ghs.axolotl",
                "app.db");


        return ReadCustomDirectory(db);
    }


    private static string? ReadCustomDirectory(string database)
    {
        if (!File.Exists(database))
            return null;


        try
        {
            using var connection =
                new SqliteConnection(
                    $"Data Source={database};Mode=ReadOnly");


            connection.Open();


            using var command =
                connection.CreateCommand();


            command.CommandText =
                """
                SELECT custom_dir
                FROM settings
                WHERE custom_dir IS NOT NULL
                LIMIT 1;
                """;


            var value =
                command.ExecuteScalar()
                    ?.ToString();


            if (string.IsNullOrWhiteSpace(value))
                return null;


            value = Path.GetFullPath(value);


            return Directory.Exists(value)
                ? value
                : null;
        }
        catch
        {
            return null;
        }
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


    private sealed record MinecraftFolderProvider(
        MinecraftFolderKind Kind,
        string Name,
        Func<string?> Reader);
}

public sealed record DetectedLauncherFolder(
    string Name,
    string Path,
    MinecraftFolderKind Kind);