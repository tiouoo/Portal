using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using Portal.Const;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

internal partial class MinecraftInstallationPage : UserControl
{
    private readonly MinecraftInstallationViewModel _viewModel;

    public MinecraftInstallationPage(VersionManifestEntry entry)
    {
        InitializeComponent();
        DataContext = _viewModel = new MinecraftInstallationViewModel(entry);
    }

    private void Install_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.HasMissingRequiredJavaRuntime)
        {
            if (TopLevel.GetTopLevel(this) is { } topLevel)
                NotificationGateway.Notice(topLevel,
                    "所选加载器需要 Java 运行时。请先在设置中添加有效的 Java，再开始安装。",
                    NotificationType.Warning);
            return;
        }

        _ = _viewModel.InstallAsync();
        _viewModel.Complete();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => _viewModel.Cancel();
}

public sealed record MinecraftInstallationDialogResult;

public partial class MinecraftInstallationViewModel : ObservableObject, INotifyDataErrorInfo, IDialogContext
{
    // 按 (游戏版本, 加载器) 缓存网络查询结果；限制容量，避免浏览大量版本后常驻内存。
    private static readonly LruCache<(string Version, LoaderKind Kind), IInstallEntry?> LatestLoaderCache = new(128);
    private readonly VersionManifestEntry _vanilla;
    private readonly Dictionary<LoaderKind, IInstallEntry> _selectedLoaders = [];
    private readonly Dictionary<LoaderKind, int> _loadGenerations = [];
    private readonly Dictionary<string, List<string>> _errors = [];
    private bool _updatingSelection;
    private int _loadingCount;

    public ObservableCollection<MinecraftFolderEntry> MinecraftFolders { get; } = [];

    public string VanillaVersion => _vanilla.Id;
    [ObservableProperty] public partial MinecraftFolderEntry? SelectedMinecraftFolder { get; set; }
    [ObservableProperty] public partial string CustomVersionId { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsInstalling { get; set; }
    [ObservableProperty] public partial bool IsFabricSelected { get; set; }
    [ObservableProperty] public partial bool IsForgeSelected { get; set; }
    [ObservableProperty] public partial bool IsNeoForgeSelected { get; set; }
    [ObservableProperty] public partial bool IsQuiltSelected { get; set; }
    [ObservableProperty] public partial bool IsOptiFineSelected { get; set; }
    [ObservableProperty] public partial string FabricStatus { get; set; } = "不安装";
    [ObservableProperty] public partial string ForgeStatus { get; set; } = "不安装";
    [ObservableProperty] public partial string NeoForgeStatus { get; set; } = "不安装";
    [ObservableProperty] public partial string QuiltStatus { get; set; } = "不安装";
    [ObservableProperty] public partial string OptiFineStatus { get; set; } = "不安装";

    public bool HasModLoader => IsFabricSelected || IsForgeSelected || IsNeoForgeSelected || IsQuiltSelected ||
                                IsOptiFineSelected;
    public bool CanCustomizeVersionId => true;
    public bool RequiresJava => IsForgeSelected || IsNeoForgeSelected || IsOptiFineSelected;
    public bool CanInstall => !IsInstalling && _loadingCount == 0 && SelectedMinecraftFolder is not null &&
                              IsVersionIdValid() && SelectedLoadersAreReady();
    public bool HasMissingRequiredJavaRuntime => RequiresJava && GetJavaPath() is null;

    public MinecraftInstallationViewModel(VersionManifestEntry vanilla)
    {
        _vanilla = vanilla;
        foreach (var folder in Data.ConfigEntry.MinecraftFolders.Where(x => x.SupportsTraditionalInstallation))
            MinecraftFolders.Add(folder);
        SelectedMinecraftFolder = Data.ConfigEntry.DefaultMinecraftFolder ?? MinecraftFolders.FirstOrDefault();
        CustomVersionId = vanilla.Id;
    }

    partial void OnSelectedMinecraftFolderChanged(MinecraftFolderEntry? value) => UpdateVersionState();
    partial void OnCustomVersionIdChanged(string value) => UpdateVersionState();
    partial void OnIsInstallingChanged(bool value) => OnPropertyChanged(nameof(CanInstall));
    partial void OnIsFabricSelectedChanged(bool value) => SelectionChanged(LoaderKind.Fabric, value);
    partial void OnIsForgeSelectedChanged(bool value) => SelectionChanged(LoaderKind.Forge, value);
    partial void OnIsNeoForgeSelectedChanged(bool value) => SelectionChanged(LoaderKind.NeoForge, value);
    partial void OnIsQuiltSelectedChanged(bool value) => SelectionChanged(LoaderKind.Quilt, value);
    partial void OnIsOptiFineSelectedChanged(bool value) => SelectionChanged(LoaderKind.OptiFine, value);

    private void SelectionChanged(LoaderKind kind, bool selected)
    {
        if (_updatingSelection) return;

        _updatingSelection = true;
        try
        {
            if (selected)
            {
                if (kind == LoaderKind.OptiFine)
                {
                    IsFabricSelected = false;
                    IsNeoForgeSelected = false;
                    IsQuiltSelected = false;
                }
                else
                {
                    IsFabricSelected = kind == LoaderKind.Fabric;
                    IsForgeSelected = kind == LoaderKind.Forge;
                    IsNeoForgeSelected = kind == LoaderKind.NeoForge;
                    IsQuiltSelected = kind == LoaderKind.Quilt;
                    if (kind != LoaderKind.Forge) IsOptiFineSelected = false;
                }
            }

            foreach (var loaderKind in Enum.GetValues<LoaderKind>())
            {
                if (!IsSelected(loaderKind))
                {
                    _selectedLoaders.Remove(loaderKind);
                    SetStatus(loaderKind, "不安装");
                    _loadGenerations[loaderKind] = _loadGenerations.GetValueOrDefault(loaderKind) + 1;
                }
            }
        }
        finally
        {
            _updatingSelection = false;
        }

        CustomVersionId = CreateRecommendedVersionId();
        UpdateVersionState();
        if (selected && IsSelected(kind)) _ = LoadLatestAsync(kind);
    }

    private async Task LoadLatestAsync(LoaderKind kind)
    {
        var generation = _loadGenerations.GetValueOrDefault(kind) + 1;
        _loadGenerations[kind] = generation;
        _loadingCount++;
        SetStatus(kind, "正在获取最新版...");
        OnPropertyChanged(nameof(CanInstall));
        try
        {
            if (!LatestLoaderCache.TryGetValue((_vanilla.Id, kind), out var entry))
            {
                entry = await FetchLatestAsync(kind);
                LatestLoaderCache.Set((_vanilla.Id, kind), entry);
            }

            if (!IsSelected(kind) || _loadGenerations.GetValueOrDefault(kind) != generation) return;
            if (entry is null)
            {
                _selectedLoaders.Remove(kind);
                SetStatus(kind, "当前游戏版本不可用");
            }
            else
            {
                _selectedLoaders[kind] = entry;
                SetStatus(kind, $"最新版：{GetLoaderVersion(kind, entry)}");
                CustomVersionId = CreateRecommendedVersionId();
            }
        }
        catch (Exception)
        {
            if (IsSelected(kind) && _loadGenerations.GetValueOrDefault(kind) == generation)
            {
                _selectedLoaders.Remove(kind);
                SetStatus(kind, "获取失败，请取消后重试");
            }
        }
        finally
        {
            _loadingCount--;
            UpdateVersionState();
        }
    }

    private async Task<IInstallEntry?> FetchLatestAsync(LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => (await FabricInstaller.EnumerableFabricAsync(_vanilla.Id)).FirstOrDefault(),
        LoaderKind.Forge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla.Id)).FirstOrDefault(),
        LoaderKind.NeoForge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla.Id, true)).FirstOrDefault(),
        LoaderKind.Quilt => (await QuiltInstaller.EnumerableQuiltAsync(_vanilla.Id)).FirstOrDefault(),
        LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(_vanilla.Id)).FirstOrDefault(),
        _ => null
    };

    public async Task InstallAsync()
    {
        if (!CanInstall || SelectedMinecraftFolder is null) return;
        var versionId = EffectiveVersionId();
        var folder = SelectedMinecraftFolder;
        var selectedEntries = _selectedLoaders.ToDictionary(x => x.Key, x => x.Value);
        var javaPath = GetJavaPath();
        IsInstalling = true;
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装 Minecraft Java {versionId}",
            Description = "正在创建安装任务",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消安装",
                    Description = "取消当前安装任务。",
                    IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) =>
                    {
                        managedTask.RequestCancellation();
                        return Task.CompletedTask;
                    },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, context => RunInstallationAsync(context, folder, versionId, selectedEntries, javaPath));
        task.Start();
        try
        {
            await task.Completion;
        }
        finally
        {
            IsInstalling = false;
            UpdateVersionState();
        }
    }

    private async Task RunInstallationAsync(TaskExecutionContext context, MinecraftFolderEntry folder, string versionId,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        await RunStepAsync(context, "验证安装配置", "正在检查安装目录、实例 ID 和 Java 运行时", async step =>
        {
            if (RequiresJava && string.IsNullOrWhiteSpace(javaPath))
                throw new InvalidOperationException("所选安装方案需要有效的 Java 运行时。");
            if (VersionDirectoryExists(versionId))
                throw new InvalidOperationException($"实例 ID “{versionId}”已存在于所选文件夹，请更换名称。");
            step.ReportProgress(1);
            await Task.CompletedTask;
        });

        var versionDirectory = Path.Combine(folder.FolderPath, "versions", versionId);
        // Mod-loader profiles must always inherit from the canonical vanilla ID.
        var vanillaId = selectedEntries.Count > 0 ? _vanilla.Id : versionId;
        var vanillaDirectory = Path.Combine(folder.FolderPath, "versions", vanillaId);
        var vanillaDirectoryExisted = Directory.Exists(vanillaDirectory);
        try
        {
            var primary = selectedEntries.FirstOrDefault(x => x.Key != LoaderKind.OptiFine);
            var primaryEntry = primary.Value;
            var primaryInstaller = primaryEntry is null
                ? null
                : CreatePrimaryInstaller(primary.Key, primaryEntry, folder.FolderPath, versionId, javaPath);
            var optifineInstaller = selectedEntries.TryGetValue(LoaderKind.OptiFine, out var optifineEntry)
                ? CreatePreloadOptifineInstaller(folder.FolderPath, (OptifineInstallEntry)optifineEntry, javaPath)
                : null;

            var vanillaTask = RunStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {_vanilla.Id}", async step =>
            {
                var installer = VanillaInstaller.Create(folder.FolderPath, _vanilla, vanillaId);
                AttachProgressReporter(installer, step);
                return await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
            });
            var preloadTasks = new List<Task>();
            if (primaryInstaller is not null)
            {
                preloadTasks.Add(RunStepAsync(context, $"预下载 {primary.Key}", $"正在并行下载 {primary.Key} 安装文件", step =>
                {
                    AttachProgressReporter(primaryInstaller, step);
                    return RunInBackgroundAsync(token => PreloadInstallerAsync(primaryInstaller, token), step.CancellationToken);
                }));
            }
            if (optifineInstaller is not null)
            {
                preloadTasks.Add(RunStepAsync(context, "预下载 OptiFine", "正在并行下载 OptiFine 安装包", step =>
                {
                    AttachProgressReporter(optifineInstaller, step);
                    return RunInBackgroundAsync(optifineInstaller.PreloadAsync, step.CancellationToken);
                }));
            }

            await Task.WhenAll([vanillaTask, .. preloadTasks]);
            var minecraft = await vanillaTask;
            if (primaryInstaller is not null)
            {
                minecraft = await RunInstallerStepAsync(context, $"安装 {primary.Key}", $"正在安装最新版 {primary.Key}", primaryInstaller);
            }

            if (optifineInstaller is not null)
            {
                var installer = primaryInstaller is not null
                    ? OptifineInstaller.Create(folder.FolderPath, (OptifineInstallEntry)optifineEntry!, minecraft)
                    : OptifineInstaller.Create(folder.FolderPath, javaPath!, (OptifineInstallEntry)optifineEntry!, versionId);
                minecraft = await RunInstallerStepAsync(context, "安装 OptiFine", "正在安装最新版 OptiFine", installer);
            }

            await RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的新实例", step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.SetDescription($"已刷新实例列表，{minecraft.Id} 已可用");
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription($"已完成 Minecraft Java {minecraft.Id} 的安装");
        }
        catch
        {
            await DeleteVersionDirectoryAsync(versionDirectory);
            if (!vanillaDirectoryExisted && vanillaDirectory != versionDirectory)
                await DeleteVersionDirectoryAsync(vanillaDirectory);
            throw;
        }
    }

    private static Task DeleteVersionDirectoryAsync(string directory) => Task.Run(() =>
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch
        {
            // Preserve the original installation or cancellation error.
        }
    });

    private static InstallerBase CreatePrimaryInstaller(LoaderKind kind, IInstallEntry entry, string folder, string versionId,
        string? javaPath) =>
        kind switch
        {
            LoaderKind.Forge or LoaderKind.NeoForge =>
                ForgeInstaller.Create(folder, javaPath!, (ForgeInstallEntry)entry, versionId),
            LoaderKind.Fabric => FabricInstaller.Create(folder, (FabricInstallEntry)entry, versionId),
            LoaderKind.Quilt => QuiltInstaller.Create(folder, (QuiltInstallEntry)entry, versionId),
            _ => throw new InvalidOperationException($"不支持的加载器：{kind}")
        };

    private static OptifineInstaller CreatePreloadOptifineInstaller(string folder, OptifineInstallEntry entry, string? javaPath) =>
        OptifineInstaller.Create(folder, javaPath!, entry);

    private static Task PreloadInstallerAsync(InstallerBase installer, CancellationToken cancellationToken) => installer switch
    {
        ForgeInstaller forge => forge.PreloadAsync(cancellationToken),
        FabricInstaller fabric => fabric.PreloadAsync(cancellationToken),
        QuiltInstaller quilt => quilt.PreloadAsync(cancellationToken),
        _ => throw new NotSupportedException($"不支持预下载的加载器：{installer.GetType().Name}")
    };

    private static async Task<MinecraftEntry> RunInstallerStepAsync(TaskExecutionContext context, string name, string description,
        InstallerBase installer) => await RunStepAsync(context, name, description, async step =>
    {
        AttachProgressReporter(installer, step);
        return await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
    });

    private static Task RunInBackgroundAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        Task.Run(() => operation(cancellationToken), cancellationToken);

    private static Task<T> RunInBackgroundAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) => Task.Run(() => operation(cancellationToken), cancellationToken);

    private static void AttachProgressReporter(InstallerBase installer, TaskExecutionContext context) =>
        installer.ProgressChanged += CreateProgressReporter(context);

    private static EventHandler<InstallProgressChangedEventArgs> CreateProgressReporter(TaskExecutionContext context)
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
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is { } current)
                    ReportInstallerProgress(context, current);
            }, DispatcherPriority.Background);
        };
    }

    private static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task> operation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var step = context.CreateChild(new TaskOptions { Name = name, Description = description, Progress = 0 }, operation);
        step.Start();
        await step.Completion;
        if (step.Exception is not null) throw new InvalidOperationException(step.Exception.Message, step.Exception);
        context.CancellationToken.ThrowIfCancellationRequested();
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
        var count = progress.TotalStepTaskCount > 0
            ? $" {progress.FinishedStepTaskCount}/{progress.TotalStepTaskCount}"
            : string.Empty;
        var speed = progress.IsStepSupportSpeed && progress.Speed >= 0
            ? $"，{FormatDownloadSpeed(progress.Speed)}"
            : string.Empty;
        context.SetDescription($"{GetInstallStepDescription(progress.StepName)}{count}{speed}");
    }

    private static string GetInstallStepDescription(InstallStep step) => step switch
    {
        InstallStep.Started => "正在准备安装器",
        InstallStep.DownloadVersionJson => "正在下载版本元数据",
        InstallStep.ParseMinecraft => "正在解析 Minecraft 版本信息",
        InstallStep.DownloadAssetIndexFile => "正在下载资源索引",
        InstallStep.DownloadLibraries => "正在下载游戏依赖文件",
        InstallStep.DownloadPackage => "正在下载加载器安装包",
        InstallStep.ParsePackage => "正在解析加载器安装包",
        InstallStep.WriteVersionJsonAndSomeDependencies => "正在写入版本与依赖配置",
        InstallStep.RunInstallProcessor => "正在运行加载器安装处理器",
        InstallStep.RanToCompletion => "安装文件已完成",
        InstallStep.Interrupted => "安装已中断",
        _ => "正在安装游戏文件"
    };

    private static string FormatDownloadSpeed(double bytesPerSecond)
    {
        string[] units = ["B/s", "KiB/s", "MiB/s", "GiB/s"];
        var value = bytesPerSecond;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private bool IsSelected(LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => IsFabricSelected,
        LoaderKind.Forge => IsForgeSelected,
        LoaderKind.NeoForge => IsNeoForgeSelected,
        LoaderKind.Quilt => IsQuiltSelected,
        LoaderKind.OptiFine => IsOptiFineSelected,
        _ => false
    };

    private void SetStatus(LoaderKind kind, string status)
    {
        switch (kind)
        {
            case LoaderKind.Fabric: FabricStatus = status; break;
            case LoaderKind.Forge: ForgeStatus = status; break;
            case LoaderKind.NeoForge: NeoForgeStatus = status; break;
            case LoaderKind.Quilt: QuiltStatus = status; break;
            case LoaderKind.OptiFine: OptiFineStatus = status; break;
        }
    }

    private static string GetLoaderVersion(LoaderKind kind, IInstallEntry entry) => kind switch
    {
        LoaderKind.Quilt => ((QuiltInstallEntry)entry).Loader.Version,
        _ => entry.DisplayVersion
    };

    private bool SelectedLoadersAreReady() => Enum.GetValues<LoaderKind>()
        .Where(IsSelected).All(_selectedLoaders.ContainsKey);

    private string EffectiveVersionId() => CustomVersionId.Trim();
    private bool IsVersionIdValid() => !_errors.ContainsKey(nameof(CustomVersionId));

    private void UpdateVersionState()
    {
        var id = EffectiveVersionId();
        var error = string.IsNullOrWhiteSpace(id)
            ? "实例 id 不能为空"
            : id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                ? "实例 id 包含文件夹名称不允许的字符"
                : SelectedMinecraftFolder is not null && VersionDirectoryExists(id)
                    ? "该实例 id 已存在，请更换名称"
                    : null;
        SetError(nameof(CustomVersionId), error);
        OnPropertyChanged(nameof(HasModLoader));
        OnPropertyChanged(nameof(CanCustomizeVersionId));
        OnPropertyChanged(nameof(RequiresJava));
        OnPropertyChanged(nameof(HasMissingRequiredJavaRuntime));
        OnPropertyChanged(nameof(CanInstall));
    }

    private bool VersionDirectoryExists(string id) => SelectedMinecraftFolder is not null &&
        Directory.Exists(Path.Combine(SelectedMinecraftFolder.FolderPath, "versions", id));

    private string CreateRecommendedVersionId()
    {
        var names = Enum.GetValues<LoaderKind>()
            .Where(IsSelected)
            .Select(kind => _selectedLoaders.TryGetValue(kind, out var entry)
                ? $"{kind}-{GetLoaderVersion(kind, entry)}"
                : kind.ToString());
        return HasModLoader ? $"{_vanilla.Id} {string.Join(" + ", names)}" : _vanilla.Id;
    }

    private static string? GetJavaPath()
    {
        var preferred = Data.ConfigEntry.DefaultJavaRuntime;
        if (preferred is { JavaPath: { } path } && File.Exists(path)) return path;
        return Data.ConfigEntry.JavaRuntimes.Select(runtime => runtime.JavaPath).FirstOrDefault(File.Exists);
    }

    public bool HasErrors => _errors.Count > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public void Complete() => RequestClose?.Invoke(this, new MinecraftInstallationDialogResult());
    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errors.TryGetValue(propertyName, out var errors) ? errors : [];

    private void SetError(string propertyName, string? error)
    {
        if (error is null) _errors.Remove(propertyName);
        else _errors[propertyName] = [error];
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
    }
}

public enum LoaderKind
{
    Fabric,
    Forge,
    NeoForge,
    Quilt,
    OptiFine
}
