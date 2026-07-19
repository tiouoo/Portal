using System.Collections.ObjectModel;
using System.Collections;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Java;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;
using Tio.Avalonia.Standard.Modules.Tasks;

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
        Loaded += async (_, _) => await _viewModel.PreloadLoadersAsync();
    }

    public PageInfo PageInfo { get; init; }

    public TabEntry HostTab { get; set; } = null!;

    private void FabricOption_OnTapped(object? sender, TappedEventArgs e) =>
        _viewModel.Select((sender as Control)?.DataContext as LoaderOption);

    private void ForgeOption_OnTapped(object? sender, TappedEventArgs e) =>
        _viewModel.Select((sender as Control)?.DataContext as LoaderOption);

    private void NeoForgeOption_OnTapped(object? sender, TappedEventArgs e) =>
        _viewModel.Select((sender as Control)?.DataContext as LoaderOption);

    private void QuiltOption_OnTapped(object? sender, TappedEventArgs e) =>
        _viewModel.Select((sender as Control)?.DataContext as LoaderOption);

    private void OptifineOption_OnTapped(object? sender, TappedEventArgs e) =>
        _viewModel.Select((sender as Control)?.DataContext as LoaderOption);

    private void Install_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = _viewModel.InstallAsync();
        HostTab.Close();
    }
}

public partial class MinecraftInstallationViewModel : ObservableObject, INotifyDataErrorInfo
{
    private static readonly Dictionary<(string Version, LoaderKind Kind), IReadOnlyList<LoaderOption>> LoaderCache = [];
    private readonly VersionManifestEntry _vanilla;
    private LoaderOption? _selectedPrimary;
    private LoaderOption? _selectedOptifine;
    private readonly Dictionary<string, List<string>> _errors = [];

    public ObservableCollection<MinecraftFolderEntry> MinecraftFolders { get; } = [];
    public ObservableCollection<JavaRuntimeEntry> JavaRuntimes { get; } = [];
    public ObservableCollection<LoaderOption> FabricOptions { get; } = [];
    public ObservableCollection<LoaderOption> ForgeOptions { get; } = [];
    public ObservableCollection<LoaderOption> NeoForgeOptions { get; } = [];
    public ObservableCollection<LoaderOption> QuiltOptions { get; } = [];
    public ObservableCollection<LoaderOption> OptifineOptions { get; } = [];

    public string VanillaVersion => _vanilla.Id;
    [ObservableProperty] public partial MinecraftFolderEntry? SelectedMinecraftFolder { get; set; }
    [ObservableProperty] public partial JavaRuntimeEntry? SelectedJavaRuntime { get; set; }
    [ObservableProperty] public partial string CustomVersionId { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsInstalling { get; set; }

    public bool HasModLoader => _selectedPrimary is not null || _selectedOptifine is not null;
    public bool CanCustomizeVersionId => true;

    public bool RequiresJava => _selectedPrimary?.Kind is LoaderKind.Forge or LoaderKind.NeoForge ||
                                _selectedOptifine is not null;

    public bool CanInstall => !IsInstalling && SelectedMinecraftFolder is not null && IsVersionIdValid();
    public string FabricHeader => HeaderFor(LoaderKind.Fabric);
    public string ForgeHeader => HeaderFor(LoaderKind.Forge);
    public string NeoForgeHeader => HeaderFor(LoaderKind.NeoForge);
    public string QuiltHeader => HeaderFor(LoaderKind.Quilt);
    public string OptiFineHeader => HeaderFor(LoaderKind.OptiFine);

    public MinecraftInstallationViewModel(VersionManifestEntry vanilla)
    {
        _vanilla = vanilla;
        foreach (var folder in Data.ConfigEntry.MinecraftFolders) MinecraftFolders.Add(folder);
        foreach (var java in Data.ConfigEntry.JavaRuntimes) JavaRuntimes.Add(java);
        SelectedMinecraftFolder = Data.ConfigEntry.DefaultMinecraftFolder ?? MinecraftFolders.FirstOrDefault();
        SelectedJavaRuntime = Data.ConfigEntry.DefaultJavaRuntime ?? JavaRuntimes.FirstOrDefault();
        CustomVersionId = vanilla.Id;
    }

    partial void OnSelectedMinecraftFolderChanged(MinecraftFolderEntry? value) => UpdateVersionState();
    partial void OnCustomVersionIdChanged(string value) => UpdateVersionState();
    partial void OnIsInstallingChanged(bool value) => OnPropertyChanged(nameof(CanInstall));

    public Task PreloadLoadersAsync() => Task.WhenAll(Enum.GetValues<LoaderKind>().Select(LoadAsync));

    private async Task LoadAsync(LoaderKind kind)
    {
        var target = GetOptions(kind);
        if (target.Count > 0) return;
        SetLoaderStatus(kind, "正在加载");
        try
        {
            var options = LoaderCache.TryGetValue((_vanilla.Id, kind), out var cached)
                ? cached
                : await FetchAsync(kind);
            LoaderCache[(_vanilla.Id, kind)] = options;
            foreach (var option in options) target.Add(option);
            SetLoaderStatus(kind, options.Count == 0 ? "没有可用版本" : null);
        }
        catch (Exception)
        {
            SetLoaderStatus(kind, "加载失败");
        }
    }

    public void Select(LoaderOption? option)
    {
        if (option is null) return;
        if (option.Kind == LoaderKind.OptiFine)
        {
            if (_selectedOptifine == option) _selectedOptifine = null;
            else _selectedOptifine = option;
        }
        else
        {
            if (_selectedPrimary == option) _selectedPrimary = null;
            else _selectedPrimary = option;
        }

        foreach (var item in AllOptions()) item.IsSelected = item == _selectedPrimary || item == _selectedOptifine;
        CustomVersionId = CreateRecommendedVersionId();
        UpdateVersionState();
    }

    public async Task InstallAsync()
    {
        if (!CanInstall || SelectedMinecraftFolder is null) return;
        var versionId = EffectiveVersionId();
        var folder = SelectedMinecraftFolder;
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
                    Description = "取消尚未开始的安装步骤。",
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
        }, context => RunInstallationAsync(context, folder, versionId));
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

    private async Task RunInstallationAsync(TaskExecutionContext context, MinecraftFolderEntry folder, string versionId)
    {
        await RunStepAsync(context, "验证安装配置", "正在检查安装目录、实例 ID 和 Java 运行时", async step =>
        {
            if (RequiresJava && (SelectedJavaRuntime is null || !File.Exists(SelectedJavaRuntime.JavaPath)))
                throw new InvalidOperationException("所选安装方案需要有效的 Java 运行时。");
            if (VersionDirectoryExists(versionId) || (HasModLoader && VersionDirectoryExists($"{versionId}-base")))
                throw new InvalidOperationException($"实例 ID “{versionId}”或其内部父版本目录已存在于所选文件夹，请更换名称。");
            step.ReportProgress(1);
            await Task.CompletedTask;
        });

        var entries = new List<IInstallEntry> { _vanilla };
        if (_selectedPrimary is not null) entries.Add(_selectedPrimary.Entry);
        if (_selectedOptifine is not null) entries.Add(_selectedOptifine.Entry);
        var installationName = entries.Count == 1 ? "安装原版 Minecraft 文件" : "安装复合加载器版本";
        await RunStepAsync(context, installationName, $"正在安装 {versionId} 的游戏与加载器文件", async step =>
        {
            step.SetDescription(entries.Count == 1
                ? $"正在下载并安装 Minecraft {versionId}"
                : $"正在组合安装 {string.Join(" + ", entries.Skip(1).Select(x => x.DisplayVersion))}");
            if (entries.Count == 1)
            {
                var installer = VanillaInstaller.Create(folder.FolderPath, _vanilla, versionId);
                installer.ProgressChanged += (_, progress) => ReportInstallerProgress(step, progress);
                await installer.InstallAsync(step.CancellationToken);
            }
            else
            {
                var installer = CompositeInstaller.Create(entries, folder.FolderPath, SelectedJavaRuntime?.JavaPath, versionId);
                installer.ProgressChanged += (_, progress) => ReportInstallerProgress(step, progress);
                await installer.InstallAsync(step.CancellationToken);
            }
            step.SetDescription($"已写入 {versionId} 的安装文件");
            step.ReportProgress(1);
        });

        await RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的新实例", step =>
        {
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders.Select(x => (x.FolderPath, x.FolderName)));
            step.SetDescription($"已刷新实例列表，{versionId} 已可用");
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
        context.SetDescription($"已完成 Minecraft Java {versionId} 的安装");
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
        InstallStep.ParseInstaller => "正在解析复合安装方案",
        InstallStep.InstallVanilla => "正在安装原版 Minecraft",
        InstallStep.InstallPrimaryModLoader => "正在安装主加载器",
        InstallStep.InstallSecondaryModLoader => "正在安装附加加载器",
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

    private async Task<IReadOnlyList<LoaderOption>> FetchAsync(LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => (await FabricInstaller.EnumerableFabricAsync(_vanilla.Id))
            .Select(x => new LoaderOption(kind, x, $"Fabric-{x.DisplayVersion}")).ToArray(),
        LoaderKind.Forge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla.Id))
            .Select(x => new LoaderOption(kind, x, $"Forge-{x.DisplayVersion}")).ToArray(),
        LoaderKind.NeoForge => (await ForgeInstaller.EnumerableForgeAsync(_vanilla.Id, true))
            .Select(x => new LoaderOption(kind, x, $"NeoForge-{x.DisplayVersion}")).ToArray(),
        LoaderKind.Quilt => (await QuiltInstaller.EnumerableQuiltAsync(_vanilla.Id))
            .Select(x => new LoaderOption(kind, x, $"Quilt-{x.Loader.Version}")).ToArray(),
        LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(_vanilla.Id))
            .Select(x => new LoaderOption(kind, x, $"OptiFine-{x.DisplayVersion}")).ToArray(),
        _ => []
    };

    private ObservableCollection<LoaderOption> GetOptions(LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => FabricOptions, LoaderKind.Forge => ForgeOptions, LoaderKind.NeoForge => NeoForgeOptions,
        LoaderKind.Quilt => QuiltOptions, LoaderKind.OptiFine => OptifineOptions,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private IEnumerable<LoaderOption> AllOptions() => FabricOptions.Concat(ForgeOptions).Concat(NeoForgeOptions)
        .Concat(QuiltOptions).Concat(OptifineOptions);

    private string EffectiveVersionId() => CustomVersionId.Trim();
    private bool IsVersionIdValid() => !_errors.ContainsKey(nameof(CustomVersionId));

    private void UpdateVersionState()
    {
        var id = EffectiveVersionId();
        var error = string.IsNullOrWhiteSpace(id)
            ? "实例 id 不能为空"
            :
            id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                ? "实例 id 包含文件夹名称不允许的字符"
                :
                HasModLoader && string.Equals(id, _vanilla.Id, StringComparison.OrdinalIgnoreCase)
                    ?
                    "附加加载器时，实例 id 不能与原版版本号相同"
                    :
                    SelectedMinecraftFolder is not null &&
                    (VersionDirectoryExists(id) || HasModLoader && VersionDirectoryExists($"{id}-base"))
                        ? "该实例 id 或内部父版本目录已存在，请更换名称"
                        : null;
        SetError(nameof(CustomVersionId), error);
        OnPropertyChanged(nameof(HasModLoader));
        OnPropertyChanged(nameof(CanCustomizeVersionId));
        OnPropertyChanged(nameof(RequiresJava));
        OnPropertyChanged(nameof(CanInstall));
    }

    private bool VersionDirectoryExists(string id) => SelectedMinecraftFolder is not null &&
                                                      Directory.Exists(Path.Combine(SelectedMinecraftFolder.FolderPath,
                                                          "versions", id));

    private string CreateRecommendedVersionId()
    {
        var names = new[] { _selectedPrimary, _selectedOptifine }
            .Where(option => option is not null)
            .Select(option => option!.DisplayName);
        return names.Any() ? $"{_vanilla.Id} {string.Join(" + ", names)}" : _vanilla.Id;
    }

    private readonly Dictionary<LoaderKind, string?> _loaderStatuses = [];

    private void SetLoaderStatus(LoaderKind kind, string? status)
    {
        _loaderStatuses[kind] = status;
        OnPropertyChanged(kind switch
        {
            LoaderKind.Fabric => nameof(FabricHeader), LoaderKind.Forge => nameof(ForgeHeader),
            LoaderKind.NeoForge => nameof(NeoForgeHeader), LoaderKind.Quilt => nameof(QuiltHeader),
            LoaderKind.OptiFine => nameof(OptiFineHeader), _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
    }

    private string HeaderFor(LoaderKind kind) => _loaderStatuses.GetValueOrDefault(kind) is { } status
        ? $"{kind} · {status}"
        : kind.ToString();

    public bool HasErrors => _errors.Count > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errors.TryGetValue(propertyName, out var errors)
            ? errors
            : [];

    private void SetError(string propertyName, string? error)
    {
        if (error is null)
            _errors.Remove(propertyName);
        else
            _errors[propertyName] = [error];
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

public partial class LoaderOption(LoaderKind kind, IInstallEntry entry, string displayName) : ObservableObject
{
    public LoaderKind Kind { get; } = kind;
    public IInstallEntry Entry { get; } = entry;
    public string DisplayName { get; } = displayName;
    [ObservableProperty] public partial bool IsSelected { get; set; }
}
