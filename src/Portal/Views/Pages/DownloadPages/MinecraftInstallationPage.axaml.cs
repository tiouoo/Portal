using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages.DownloadPages;

public partial class MinecraftInstallationPage : UserControl, ITioTabPage
{
    private readonly MinecraftInstallationViewModel _viewModel;

    public MinecraftInstallationPage(VersionManifestEntry entry)
    {
        InitializeComponent();
        PageInfo = new PageInfo
        {
            Title = $"安装 Minecraft Java {entry.Id}",
            Icon = StreamGeometry.Parse(
                "F1 M640,640z M0,0z M217.6,544L451.3,544 566.7,339.8 268.4,397.2 217.6,544z M569,304.1L451.4,96 219.9,96 424.5,331.9 569,304.1z M188.6,112.8L71.5,320 187.5,525.2 289.9,229.6 188.6,112.8z")
        };
        DataContext = _viewModel = new MinecraftInstallationViewModel(entry);
    }

    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; } = null!;

    private void Install_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = _viewModel.InstallAsync();
        HostTab.Close();
    }
}

public partial class MinecraftInstallationViewModel : ObservableObject, INotifyDataErrorInfo
{
    private static readonly Dictionary<(string Version, LoaderKind Kind), IInstallEntry?> LatestLoaderCache = [];
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
    public bool CanCustomizeVersionId => HasModLoader;
    public bool RequiresJava => IsForgeSelected || IsNeoForgeSelected || IsOptiFineSelected;
    public bool CanInstall => !IsInstalling && _loadingCount == 0 && SelectedMinecraftFolder is not null &&
                              IsVersionIdValid() && HasRequiredJavaRuntime() && SelectedLoadersAreReady();

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
                LatestLoaderCache[(_vanilla.Id, kind)] = entry;
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

        MinecraftEntry minecraft = null!;
        await RunStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {_vanilla.Id}", async step =>
        {
            var installer = VanillaInstaller.Create(folder.FolderPath, _vanilla);
            installer.ProgressChanged += (_, progress) => ReportInstallerProgress(step, progress);
            minecraft = await installer.InstallAsync(step.CancellationToken);
        });

        var primary = selectedEntries.FirstOrDefault(x => x.Key != LoaderKind.OptiFine);
        if (primary.Value is not null)
        {
            await RunStepAsync(context, $"安装 {primary.Key}", $"正在安装最新版 {primary.Key}", async step =>
            {
                var installer = CreatePrimaryInstaller(primary.Key, primary.Value, folder.FolderPath, versionId, javaPath);
                installer.ProgressChanged += (_, progress) => ReportInstallerProgress(step, progress);
                minecraft = await installer.InstallAsync(step.CancellationToken);
            });
        }

        if (selectedEntries.TryGetValue(LoaderKind.OptiFine, out var optifineEntry))
        {
            await RunStepAsync(context, "安装 OptiFine", "正在安装最新版 OptiFine", async step =>
            {
                var entry = (OptifineInstallEntry)optifineEntry;
                var installer = primary.Value is not null
                    ? OptifineInstaller.Create(folder.FolderPath, entry, minecraft)
                    : OptifineInstaller.Create(folder.FolderPath, javaPath!, entry, versionId);
                installer.ProgressChanged += (_, progress) => ReportInstallerProgress(step, progress);
                minecraft = await installer.InstallAsync(step.CancellationToken);
            });
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

    private string EffectiveVersionId() => HasModLoader ? CustomVersionId.Trim() : _vanilla.Id;
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

    private bool HasRequiredJavaRuntime() => !RequiresJava || GetJavaPath() is not null;

    private static string? GetJavaPath()
    {
        var preferred = Data.ConfigEntry.DefaultJavaRuntime;
        if (preferred is { JavaPath: { } path } && File.Exists(path)) return path;
        return Data.ConfigEntry.JavaRuntimes.Select(runtime => runtime.JavaPath).FirstOrDefault(File.Exists);
    }

    public bool HasErrors => _errors.Count > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

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
