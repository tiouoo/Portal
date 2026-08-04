using Avalonia.Controls;
using System.Diagnostics;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Installer;
using MinecraftLaunch.Components.Installer.Modpack;
using MinecraftLaunch.Components.Provider;
using MinecraftLaunch.Utilities;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Operations.Java;
using Portal.Services;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class ModpackDetailsPage : UserControl, ITioTabPage
{
    private JavaResourceVersionGroup? _targetVersionGroup;
    private bool _isWaitingForTargetVersionGroup;
    public ModpackDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.Modpack, ModDetailsSource.Modrinth, string.Empty)) { }
    public ModpackDetailsPage(JavaResourceDetailsTarget target)
    {
        InitializeComponent(); ViewModel = new ModpackDetailsPageViewModel(target); DataContext = ViewModel;
        ViewModel.TargetVersionGroupReady += ScrollToTargetVersionGroup;
        PageInfo = new PageInfo { Title = "整合包详情", Icon = StreamGeometry.Parse(JavaResourceDetailsIcon.Data) };
        Loaded += async (_, _) =>
        {
            Logger.Info($"[Modpack] Details page loaded for {target.Source} project {target.ProjectId}.");
            await ViewModel.LoadAsync();
        };
    }
    public ModpackDetailsPageViewModel ViewModel { get; }
    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; }
    public void OnClose()
    {
        Logger.Info($"[Modpack] Details page closing for {ViewModel.Name}.");
        ViewModel.TargetVersionGroupReady -= ScrollToTargetVersionGroup;
        LayoutUpdated -= OnLayoutUpdated;
        _targetVersionGroup = null;
        _isWaitingForTargetVersionGroup = false;
        ViewModel.Dispose();
        DataContext = null;
    }
    private async void VersionFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: JavaResourceFileItem file } ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var result = await OverlayDialog.ShowCustomAsync<ModpackInstallDialog, ModpackInstallDialogViewModel,
            ModpackInstallDialogResult>(new ModpackInstallDialogViewModel(ViewModel.Name),
            topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "下载整合包", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result is null) return;
        if (result.Destination == ModpackDownloadDestination.SaveAs)
        {
            await SaveAsAsync(topLevel, file);
            return;
        }
        if (result.Folder is null || string.IsNullOrWhiteSpace(result.InstanceId)) return;
        StartInstallation(topLevel, ViewModel.Target.Source, file, ViewModel.IconUrl, result);
    }
    private void ScrollToTargetVersionGroup(JavaResourceVersionGroup group)
    {
        _targetVersionGroup = group;
        if (_isWaitingForTargetVersionGroup) return;
        _isWaitingForTargetVersionGroup = true;
        LayoutUpdated += OnLayoutUpdated;
    }
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_targetVersionGroup is null) return;
        var expander = this.GetVisualDescendants().OfType<TioExpander>()
            .FirstOrDefault(control => ReferenceEquals(control.DataContext, _targetVersionGroup));
        if (expander is null) return;
        LayoutUpdated -= OnLayoutUpdated;
        _isWaitingForTargetVersionGroup = false;
        _targetVersionGroup = null;
        expander.IsExpanded = true;
        Dispatcher.UIThread.Post(() => expander.BringIntoView(), DispatcherPriority.Render);
    }
    public static void Open(TopLevel sender, JavaResourceDetailsTarget target, string title)
    {
        if (sender is not TioTabWindowBase window || string.IsNullOrWhiteSpace(target.ProjectId)) return;
        var tab = new TabEntry(window, new ModpackDetailsPage(target), title: title); window.CreateTab(tab); window.SelectTab(tab);
    }

    public static async Task InstallLocalAsync(TopLevel topLevel, string archivePath, ModDetailsSource source,
        string suggestedInstanceId)
    {
        var result = await OverlayDialog.ShowCustomAsync<ModpackInstallDialog, ModpackInstallDialogViewModel,
            ModpackInstallDialogResult>(new ModpackInstallDialogViewModel(
                string.IsNullOrWhiteSpace(suggestedInstanceId) ? Path.GetFileNameWithoutExtension(archivePath) : suggestedInstanceId,
                false),
            topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "安装整合包", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result?.Folder is null || string.IsNullOrWhiteSpace(result.InstanceId)) return;

        var displayName = Path.GetFileName(archivePath);
        Logger.Info($"[Modpack] Queuing local {source} modpack installation from {archivePath} to {result.Folder.FolderPath} as {result.InstanceId}.");
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装整合包：{displayName}", Description = "正在准备安装", Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消安装", Description = "取消此整合包安装", IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) => { managedTask.RequestCancellation(); return Task.CompletedTask; },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, context => InstallLocalArchiveAsync(context, source, archivePath, result.Folder!.FolderPath, result.InstanceId!));
        task.Start();
        _ = ObserveInstallationAsync(task, topLevel, displayName);
    }

    public static async Task TryInstallFromPath(TopLevel topLevel, string path)
    {
        if (!TryGetModpack(path, out var archivePath, out var source, out var suggestedInstanceId))
        {
            Logger.Warning($"[Modpack] Rejected invalid local modpack archive {path}.");
            topLevel.Notice("无效整合包文件", NotificationType.Error);
            return;
        }
        
        var result = await OverlayDialog.ShowCustomAsync<ModpackInstallDialog, ModpackInstallDialogViewModel,
            ModpackInstallDialogResult>(new ModpackInstallDialogViewModel(
                string.IsNullOrWhiteSpace(suggestedInstanceId) ? Path.GetFileNameWithoutExtension(archivePath) : suggestedInstanceId,
                false),
            topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "安装整合包", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result?.Folder is null || string.IsNullOrWhiteSpace(result.InstanceId)) return;

        var displayName = Path.GetFileName(archivePath);
        Logger.Info($"[Modpack] Queuing imported {source} modpack {archivePath} to {result.Folder.FolderPath} as {result.InstanceId}.");
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装整合包：{displayName}", Description = "正在准备安装", Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消安装", Description = "取消此整合包安装", IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) => { managedTask.RequestCancellation(); return Task.CompletedTask; },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, context => InstallLocalArchiveAsync(context, source, archivePath, result.Folder!.FolderPath, result.InstanceId!));
        task.Start();
        _ = ObserveInstallationAsync(task, topLevel, displayName);
    }

    public static bool TryGetModpack(string path, out string archivePath, out ModDetailsSource source,
        out string suggestedInstanceId)
    {
        archivePath = string.Empty;
        source = default;
        suggestedInstanceId = string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            if (extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                var entry = ModrinthModpackInstaller.ParseModpackInstallEntry(path);
                archivePath = path;
                source = ModDetailsSource.Modrinth;
                suggestedInstanceId = entry.Name;
                return true;
            }

            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var entry = CurseforgeModpackInstaller.ParseModpackInstallEntry(path);
                archivePath = path;
                source = ModDetailsSource.CurseForge;
                suggestedInstanceId = entry.Id;
                return true;
            }
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Modpack] Failed to inspect local archive {path}: {exception}");
        }

        return false;
    }
    
    private static async Task SaveAsAsync(TopLevel topLevel, JavaResourceFileItem file)
    {
        var selected = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存为整合包", SuggestedFileName = file.FileName,
            FileTypeChoices = [new FilePickerFileType("整合包") { Patterns = ["*.mrpack", "*.zip"] }]
        });
        var destination = selected?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destination)) return;
        Logger.Info($"[Modpack] Exporting {file.FileName} to {destination}.");
        JavaResourceDownload.StartDownload(topLevel, JavaResourceDefinitions.Modpack, file, destination);
    }

    private static void StartInstallation(TopLevel topLevel, ModDetailsSource source, JavaResourceFileItem file,
        string? iconUrl, ModpackInstallDialogResult selection)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装整合包：{file.DisplayName}", Description = "正在准备安装", Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消安装", Description = "取消此整合包安装", IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) => { managedTask.RequestCancellation(); return Task.CompletedTask; },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, context => InstallAsync(context, source, file, iconUrl, selection));
        task.Start();
        _ = ObserveInstallationAsync(task, topLevel, file.DisplayName);
    }

    private static async Task InstallAsync(TaskExecutionContext context, ModDetailsSource source, JavaResourceFileItem file,
        string? iconUrl, ModpackInstallDialogResult selection)
    {
        var folder = selection.Folder!.FolderPath;
        var instanceId = selection.InstanceId!;
        var instancePath = Path.Combine(folder, "versions", instanceId);
        if (Directory.Exists(instancePath)) throw new InvalidOperationException($"实例 ID “{instanceId}”已存在。");
        var temporaryFolder = Path.Combine(Path.GetTempPath(), "Portal", "modpacks", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(temporaryFolder, Path.GetFileName(file.FileName));
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[Modpack] Installing remote {source} modpack {file.FileName} to {instancePath}.");
        try
        {
            await Task.Run(() => Directory.CreateDirectory(temporaryFolder));
            await RunStepAsync(context, "下载整合包安装包", $"正在下载：{file.FileName}",
                step => DownloadArchiveAsync(step, file, archivePath));
            MinecraftEntry minecraft = source switch
            {
                ModDetailsSource.Modrinth => await InstallModrinthAsync(context, folder, instanceId, archivePath,
                    GetForgeJavaPath()),
                ModDetailsSource.CurseForge => await InstallCurseForgeAsync(context, folder, instanceId, archivePath,
                    GetForgeJavaPath()),
                _ => throw new NotSupportedException("不支持的整合包来源。")
            };
            await TrySaveProjectIconAsync(iconUrl, instancePath, context.CancellationToken);
            await RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的新实例", step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.SetDescription($"已刷新实例列表，{minecraft.Id} 已可用");
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription($"整合包 {minecraft.Id} 安装完成");
            Logger.Info($"[Modpack] Installed remote modpack {file.FileName} as {minecraft.Id} in {stopwatch.Elapsed}.");
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Modpack] Remote installation of {file.FileName} was cancelled after {stopwatch.Elapsed}: {exception}");
            await DeleteDirectoryAsync(instancePath);
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            await DeleteDirectoryAsync(instancePath);
            throw;
        }
        finally
        {
            await DeleteDirectoryAsync(temporaryFolder);
        }
    }

    internal static async Task InstallLocalArchiveAsync(TaskExecutionContext context, ModDetailsSource source, string archivePath,
        string folder, string instanceId)
    {
        var instancePath = Path.Combine(folder, "versions", instanceId);
        if (Directory.Exists(instancePath)) throw new InvalidOperationException($"实例 ID “{instanceId}”已存在。");
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[Modpack] Installing local {source} modpack {archivePath} to {instancePath}.");

        try
        {
            var minecraft = source switch
            {
                ModDetailsSource.Modrinth => await InstallModrinthAsync(context, folder, instanceId, archivePath, GetForgeJavaPath()),
                ModDetailsSource.CurseForge => await InstallCurseForgeAsync(context, folder, instanceId, archivePath, GetForgeJavaPath()),
                _ => throw new NotSupportedException("不支持的整合包来源。")
            };
            await RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的新实例", step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.SetDescription($"已刷新实例列表，{minecraft.Id} 已可用");
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription($"整合包 {minecraft.Id} 安装完成");
            Logger.Info($"[Modpack] Installed local modpack {archivePath} as {minecraft.Id} in {stopwatch.Elapsed}.");
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Modpack] Local installation of {archivePath} was cancelled after {stopwatch.Elapsed}: {exception}");
            await DeleteDirectoryAsync(instancePath);
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            await DeleteDirectoryAsync(instancePath);
            throw;
        }
    }

    private static Task DeleteDirectoryAsync(string directory) => Task.Run(() =>
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        // Preserve the original installation error if an antivirus or another process holds a partial file.
        catch (Exception exception) { Logger.Warning($"[Modpack] Failed to clean up {directory}: {exception}"); }
    });

    private static async Task DownloadArchiveAsync(TaskExecutionContext context, JavaResourceFileItem file, string destination)
    {
        context.SetRunning($"正在下载：{file.FileName}");
        var reportProgress = CreateDownloadProgressReporter(context);
        var request = new DownloadRequest(file.DownloadUrl, destination, file.FileSize)
        {
            ProgressChanged = reportProgress
        };
        var result = await new DefaultDownloader().DownloadAsync(request, context.CancellationToken);
        if (result.Type == DownloadResultType.Cancelled) throw new OperationCanceledException(context.CancellationToken);
        if (result.Type != DownloadResultType.Successful) throw result.Exception ?? new IOException("整合包下载失败。");
    }

    internal static async Task TrySaveProjectIconAsync(string? iconUrl, string instancePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(iconUrl)) return;

        try
        {
            Logger.Info($"[Modpack] Downloading optional project icon from {iconUrl} to {instancePath}.");
            using var response = await HttpUtil.Client.GetAsync(iconUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            Directory.CreateDirectory(instancePath);
            var iconPath = Path.Combine(instancePath, "Portal.Icon.png");
            var temporaryPath = iconPath + ".tmp";
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = File.Create(temporaryPath))
                await source.CopyToAsync(output, cancellationToken);
            File.Move(temporaryPath, iconPath, true);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Modpack] Optional project icon download was cancelled: {exception}");
            throw;
        }
        // A project cover is optional and must never invalidate a completed installation.
        catch (Exception exception) { Logger.Warning($"[Modpack] Failed to save optional project icon to {instancePath}: {exception}"); }
    }

    private static async Task<MinecraftEntry> InstallModrinthAsync(TaskExecutionContext context, string folder, string id,
        string archivePath, string? javaPath)
    {
        var entry = await RunStepAsync(context, "解析整合包", "正在读取 Modrinth 整合包清单", step =>
        {
            return Task.Run(() =>
            {
                var parsed = ModrinthModpackInstaller.ParseModpackInstallEntry(archivePath);
                step.ReportProgress(1);
                return parsed;
            }, step.CancellationToken);
        });
        var loaderTask = RunStepAsync(context, "准备模组加载器", "正在获取整合包指定的加载器", step =>
            ModrinthModpackInstaller.ParseModLoaderEntryAsync(entry, step.CancellationToken));
        var vanillaTask = GetVanillaEntryAsync(context, entry.McVersion);
        await Task.WhenAll(loaderTask, vanillaTask);
        var loader = await loaderTask;
        var vanilla = await vanillaTask;
        javaPath = await EnsureJavaRuntimeAsync(loader, javaPath, entry.McVersion, context, context.CancellationToken);
        var vanillaInstallation = RunInstallerStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {entry.McVersion}",
            VanillaInstaller.Create(folder, vanilla));
        var filesInstallation = RunModpackFilesStepAsync(context, "安装整合包文件", "正在并行下载整合包模组",
            new ModrinthModpackInstaller
            {
                MinecraftFolder = folder, ModpackPath = archivePath, Entry = entry, Minecraft = null!,
                WorkingPath = Path.Combine(folder, "versions", id)
            });
        var minecraft = await vanillaInstallation;
        minecraft = await RunInstallerStepAsync(context, $"安装 {GetLoaderName(loader)}", "正在安装整合包指定的加载器",
            CreateModLoaderInstaller(loader, folder, id, javaPath, minecraft));
        await filesInstallation;
        return minecraft;
    }

    private static async Task<MinecraftEntry> InstallCurseForgeAsync(TaskExecutionContext context, string folder, string id,
        string archivePath, string? javaPath)
    {
        var entry = await RunStepAsync(context, "解析整合包", "正在读取 CurseForge 整合包清单", step =>
        {
            return Task.Run(() =>
            {
                var parsed = CurseforgeModpackInstaller.ParseModpackInstallEntry(archivePath);
                step.ReportProgress(1);
                return parsed;
            }, step.CancellationToken);
        });
        var loadersTask = RunStepAsync(context, "准备模组加载器", "正在获取整合包指定的加载器", async step =>
        {
            var result = new List<IInstallEntry>();
            await foreach (var loader in CurseforgeModpackInstaller.ParseModLoaderEntryByManifestAsync(entry, step.CancellationToken))
                result.Add(loader);
            step.ReportProgress(1);
            return result;
        });
        var vanillaTask = GetVanillaEntryAsync(context, entry.McVersion);
        await Task.WhenAll(loadersTask, vanillaTask);
        var loaders = await loadersTask;
        var vanilla = await vanillaTask;
        foreach (var loader in loaders)
            javaPath = await EnsureJavaRuntimeAsync(loader, javaPath, entry.McVersion, context, context.CancellationToken);
        var vanillaInstallation = RunInstallerStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {entry.McVersion}",
            VanillaInstaller.Create(folder, vanilla));
        var filesInstallation = RunModpackFilesStepAsync(context, "安装整合包文件", "正在并行解析并下载整合包模组",
            new CurseforgeModpackInstaller
            {
                MinecraftFolder = folder, ModpackPath = archivePath, Entry = entry, Minecraft = null!,
                WorkingPath = Path.Combine(folder, "versions", id)
            });
        var minecraft = await vanillaInstallation;
        foreach (var loader in loaders)
            minecraft = await RunInstallerStepAsync(context, $"安装 {GetLoaderName(loader)}", "正在安装整合包指定的加载器",
                CreateModLoaderInstaller(loader, folder, id, javaPath, minecraft));
        await filesInstallation;
        return minecraft;
    }

    private static async Task<string?> EnsureJavaRuntimeAsync(IInstallEntry loader, string? javaPath,
        string minecraftVersion, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        if (loader is not ForgeInstallEntry || !string.IsNullOrWhiteSpace(javaPath) && File.Exists(javaPath))
            return javaPath;
        var runtime = await JavaAutoInstallCoordinator.EnsureAsync(
            MinecraftInstallationViewModel.GetRecommendedJavaVersion(minecraftVersion),
            progress => ReportJavaInstallProgress(context, progress), cancellationToken);
        return runtime?.JavaPath ?? throw new InvalidOperationException("该整合包使用 Forge 或 NeoForge，需要有效的 Java 运行时。");
    }

    private static void ReportJavaInstallProgress(TaskExecutionContext context, JavaInstallProgress progress)
    {
        if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
            try
            {
                context.ReportProgress(progress.Fraction);
                context.SetDescription(progress.SpeedBytesPerSecond > 0
                    ? $"{progress.Stage}，下载速度：{DefaultDownloader.FormatSize(progress.SpeedBytesPerSecond, true)}"
                    : progress.Stage);
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private static string? GetForgeJavaPath()
    {
        var preferred = Data.ConfigEntry.DefaultJavaRuntime;
        if (preferred is { JavaPath: { } path } && File.Exists(path)) return path;
        return Data.ConfigEntry.JavaRuntimes.Select(runtime => runtime.JavaPath).FirstOrDefault(File.Exists);
    }

    private static async Task<VersionManifestEntry> GetVanillaEntryAsync(TaskExecutionContext context, string minecraftVersion) =>
        await RunStepAsync(context, "准备原版 Minecraft", $"正在查找 Minecraft {minecraftVersion}", async step =>
        {
            var version = (await VanillaInstaller.EnumerableMinecraftAsync(step.CancellationToken))
                .FirstOrDefault(candidate => candidate.Id == minecraftVersion);
            if (version is null) throw new InvalidOperationException("未找到整合包要求的 Minecraft 版本。");
            step.ReportProgress(1);
            return version;
        });

    private static InstallerBase CreateModLoaderInstaller(IInstallEntry entry, string folder, string id, string? javaPath,
        MinecraftEntry inheritedMinecraft) => entry switch
    {
        ForgeInstallEntry forge => new ForgeInstaller
        {
            MinecraftFolder = folder, JavaPath = javaPath!, Entry = forge, CustomId = id, InheritedMinecraft = inheritedMinecraft
        },
        FabricInstallEntry fabric => new FabricInstaller
        {
            MinecraftFolder = folder, Entry = fabric, CustomId = id, InheritedMinecraft = inheritedMinecraft
        },
        QuiltInstallEntry quilt => new QuiltInstaller
        {
            MinecraftFolder = folder, Entry = quilt, CustomId = id, InheritedMinecraft = inheritedMinecraft
        },
        _ => throw new NotSupportedException($"不支持整合包加载器：{entry.GetType().Name}")
    };

    private static string GetLoaderName(IInstallEntry entry) => entry switch
    {
        ForgeInstallEntry forge => forge.IsNeoforge ? "NeoForge" : "Forge",
        FabricInstallEntry => "Fabric",
        QuiltInstallEntry => "Quilt",
        _ => "模组加载器"
    };

    private static async Task<MinecraftEntry> RunInstallerStepAsync(TaskExecutionContext context, string name, string description,
        InstallerBase installer) => await RunStepAsync(context, name, description, async step =>
    {
        Exception? installationFailure = null;
        installer.ProgressChanged += CreateInstallerProgressReporter(step);
        installer.Completed += (_, completed) =>
        {
            if (!completed.IsSuccessful)
                installationFailure ??= completed.Exception ?? new InvalidOperationException("MinecraftLaunch 安装器未返回失败原因。");
        };
        try
        {
            var minecraft = await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
            if (installationFailure is not null) throw new InvalidOperationException($"{name}失败。", installationFailure);
            return minecraft;
        }
        catch when (installationFailure is not null)
        {
            throw new InvalidOperationException($"{name}失败。", installationFailure);
        }
    });

    private static Task RunModpackFilesStepAsync(TaskExecutionContext context, string name, string description,
        InstallerBase installer) => RunStepAsync(context, name, description, async step =>
    {
        installer.ProgressChanged += CreateInstallerProgressReporter(step);

        switch (installer)
        {
            case ModrinthModpackInstaller modrinth:
                await RunInBackgroundAsync(modrinth.InstallFilesAsync, step.CancellationToken);
                break;
            case CurseforgeModpackInstaller curseforge:
                await RunInBackgroundAsync(curseforge.InstallFilesAsync, step.CancellationToken);
                break;
            default:
                throw new NotSupportedException($"不支持预下载的整合包安装器：{installer.GetType().Name}");
        }
    });

    private static Task RunInBackgroundAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        Task.Run(() => operation(cancellationToken), cancellationToken);

    private static Task<T> RunInBackgroundAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) => Task.Run(() => operation(cancellationToken), cancellationToken);

    private static Action<ResourceDownloadProgressChangedEventArgs> CreateDownloadProgressReporter(TaskExecutionContext context)
    {
        ResourceDownloadProgressChangedEventArgs? latestProgress = null;
        var dispatchQueued = 0;
        return progress =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

            Volatile.Write(ref latestProgress, progress);
            if (Interlocked.Exchange(ref dispatchQueued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dispatchQueued, 0);
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is { } current)
                {
                    context.ReportProgress(current.TotalBytes > 0
                        ? Math.Clamp((double)current.DownloadedBytes / current.TotalBytes, 0, 1)
                        : null);
                    context.SetDescription($"正在下载安装包：{DefaultDownloader.FormatSize(current.Speed, true)}");
                }
            }, DispatcherPriority.Background);
        };
    }

    private static EventHandler<InstallProgressChangedEventArgs> CreateInstallerProgressReporter(TaskExecutionContext context)
    {
        InstallProgressChangedEventArgs? latestProgress = null;
        var dispatchQueued = 0;
        return (_, progress) =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

            Volatile.Write(ref latestProgress, progress);
            if (Interlocked.Exchange(ref dispatchQueued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dispatchQueued, 0);
                // Installer events can arrive after the child has completed or been cancelled.
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is { } current)
                    ReportInstallerProgress(context, current);
            }, DispatcherPriority.Background);
        };
    }

    private static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task> operation)
    {
        var step = context.CreateChild(new TaskOptions { Name = name, Description = description, Progress = 0 }, operation);
        step.Start();
        await step.Completion;
        if (step.Exception is null) return;
        context.LogError($"子任务“{name}”失败。", step.Exception);
        throw new InvalidOperationException(step.Exception.Message, step.Exception);
    }

    private static async Task<T> RunStepAsync<T>(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task<T>> operation)
    {
        T? result = default;
        await RunStepAsync(context, name, description, async step =>
        {
            result = await operation(step);
        });
        return result!;
    }

    private static void ReportInstallerProgress(TaskExecutionContext context, InstallProgressChangedEventArgs progress)
    {
        context.ReportProgress(progress.Progress);
        var count = progress.TotalStepTaskCount > 0 ? $" {progress.FinishedStepTaskCount}/{progress.TotalStepTaskCount}" : string.Empty;
        var speed = progress.IsStepSupportSpeed && progress.Speed >= 0
            ? $"，{DefaultDownloader.FormatSize(progress.Speed, true)}" : string.Empty;
        context.SetDescription($"{GetInstallStepDescription(progress.StepName)}{count}{speed}");
    }

    private static string GetInstallStepDescription(InstallStep step, InstallStep primaryStep = InstallStep.Undefined) => step switch
    {
        InstallStep.DownloadVersionJson => "正在下载版本元数据",
        InstallStep.ParseMinecraft => "正在解析 Minecraft 版本",
        InstallStep.DownloadAssetIndexFile => "正在下载资源索引",
        InstallStep.DownloadLibraries => "正在下载游戏依赖",
        InstallStep.DownloadPackage => "正在下载加载器安装包",
        InstallStep.ParsePackage => "正在解析加载器安装包",
        InstallStep.WriteVersionJsonAndSomeDependencies => "正在写入加载器配置",
        InstallStep.RunInstallProcessor => "正在运行加载器安装处理器",
        InstallStep.ParseDownloadUrls => "正在解析模组下载地址",
        InstallStep.RedirectInvalidMod => "正在处理模组下载地址",
        InstallStep.DownloadMods => "正在下载整合包模组",
        InstallStep.ExtractModpack => "正在释放整合包文件",
        _ when primaryStep != InstallStep.Undefined => GetInstallStepDescription(primaryStep),
        _ => "正在安装整合包"
    };

    internal static async Task ObserveInstallationAsync(ManagedTask task, TopLevel topLevel, string name)
    {
        var stopwatch = Stopwatch.StartNew();
        try { await task.Completion; }
        catch (OperationCanceledException exception) { Logger.Debug($"[Modpack] Installation {name} was cancelled after {stopwatch.Elapsed}: {exception}"); }
        catch (Exception exception) { Logger.Error(exception); }
        if (task.Status == ManagedTaskStatus.Completed)
        {
            Logger.Info($"[Modpack] Installation {name} completed in {stopwatch.Elapsed}.");
            Dispatcher.UIThread.Post(() => NotificationGateway.Notice(topLevel, $"{name} 安装完成", NotificationType.Success));
        }
        else if (task.Status == ManagedTaskStatus.Faulted)
            Dispatcher.UIThread.Post(() => NotificationGateway.Notice(topLevel,
                $"{name} 安装失败：{GetRootCauseMessage(task.Exception) ?? task.ErrorMessage ?? "请查看任务日志"}", NotificationType.Error));
    }

    private static string? GetRootCauseMessage(Exception? exception)
    {
        while (exception?.InnerException is not null) exception = exception.InnerException;
        return exception?.Message;
    }
}
