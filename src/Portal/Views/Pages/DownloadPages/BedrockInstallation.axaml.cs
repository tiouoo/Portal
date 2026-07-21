using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Bedrock.Standard.Interface;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public partial class BedrockInstallation : UserControl
{
    private readonly BedrockInstallationViewModel _viewModel;

    public BedrockInstallation()
    {
        InitializeComponent();
        DataContext = _viewModel = new BedrockInstallationViewModel();
        Loaded += async (_, _) => await _viewModel.LoadVersionsAsync();
    }

    private async void VersionCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed ||
            sender is not Control { DataContext: BedrockGdkVersion version }) return;

        var details = _viewModel.GetInstallDetails(version);
        if (details is null)
        {
            _viewModel.StatusText = "请先在设置中添加一个标准游戏目录。";
            return;
        }

        var result = await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Avalonia.Thickness(24),
            Text = details,
            TextWrapping = TextWrapping.Wrap
        }, null, null, new OverlayDialogOptions
        {
            Title = $"安装 Minecraft 基岩版 {version.Id}",
            Buttons = DialogButton.YesNo,
            OverrideYesButtonText = "开始安装",
            OverrideNoButtonText = "取消",
            CanLightDismiss = false,
            CanResize = false
        });
        if (result == DialogResult.Yes) _ = _viewModel.InstallAsync(version);
    }
}

public partial class BedrockInstallationViewModel : ObservableObject
{
    private readonly List<BedrockGdkVersion> _allVersions = [];

    public ObservableCollection<BedrockGdkVersion> Versions { get; } = [];

    [ObservableProperty] public partial int SelectedReleaseChannel { get; set; } = 1;
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsInstalling { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "正在获取 GDK 版本列表...";

    public bool CanInstall => !IsInstalling && !IsLoading && BedrockInstallationService.DefaultInstaller is not null &&
                              GetInstallFolder() is not null;
    partial void OnIsInstallingChanged(bool value) => UpdateInstallState();
    partial void OnIsLoadingChanged(bool value) => UpdateInstallState();
    partial void OnSelectedReleaseChannelChanged(int value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public async Task LoadVersionsAsync()
    {
        if (IsLoading || BedrockInstallationService.DefaultInstaller is not { } installer) return;

        IsLoading = true;
        var loaded = false;
        StatusText = "正在从版本源获取 GDK 版本列表...";
        try
        {
            var versions = await installer.GetGdkVersionsAsync(false, CancellationToken.None);
            _allVersions.Clear();
            _allVersions.AddRange(versions);
            loaded = true;
        }
        catch (Exception exception)
        {
            StatusText = $"无法获取 GDK 版本列表：{exception.Message}";
        }
        finally
        {
            IsLoading = false;
            if (loaded) ApplyFilter();
        }
    }

    public async Task InstallAsync(BedrockGdkVersion version)
    {
        if (!CanInstall || GetInstallFolder() is not { } folder || BedrockInstallationService.DefaultInstaller is null) return;

        var installer = BedrockInstallationService.DefaultInstaller;
        var instanceName = version.Id;
        var destination = Path.Combine(folder.FolderPath, "bedrock_versions", instanceName);
        IsInstalling = true;

        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装 Minecraft 基岩版 {instanceName}",
            Description = "正在准备 GDK 安装包",
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
        }, async context =>
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(destination))
                throw new InvalidOperationException("目标实例已存在，请更换实例名称。");

            var progress = new Progress<BedrockInstallProgress>(update =>
            {
                var value = update.Total > 0 ? (double)update.Current / update.Total : 0;
                context.ReportProgress(value);
                context.SetDescription(update.State switch
                {
                    "Downloading" when update.Total > 0 => $"正在下载 {update.Item} ({value:P0})",
                    "Downloading" => $"正在下载 {update.Item}",
                    "Extracting" when !string.IsNullOrWhiteSpace(update.Item) => $"正在解压 {update.Item}",
                    "Extracting" => "正在解压 GDK 安装包",
                    _ => $"安装状态：{update.State}"
                });
            });

            await installer.InstallGdkAsync(new BedrockOnlineInstallRequest(
                version, destination, context.CancellationToken), progress);
            context.ReportProgress(1);
            context.SetDescription("正在刷新已安装实例");
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        });

        task.Start();
        try
        {
            await task.Completion;
            StatusText = $"{instanceName} 已安装完成。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "安装已取消。";
        }
        catch (Exception exception)
        {
            StatusText = $"安装失败：{exception.Message}";
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void UpdateInstallState()
    {
        if (BedrockInstallationService.DefaultInstaller is null)
            StatusText = "基岩版安装仅支持 Windows。";
        else if (GetInstallFolder() is null)
            StatusText = "请先在设置中添加一个标准游戏目录。";
        else if (!IsLoading && !IsInstalling && _allVersions.Count == 0)
            StatusText = "没有可用的 GDK 版本。";

        OnPropertyChanged(nameof(CanInstall));
    }

    private void ApplyFilter()
    {
        IEnumerable<BedrockGdkVersion> versions = _allVersions;
        versions = SelectedReleaseChannel switch
        {
            1 => versions.Where(version => !version.IsPreview),
            2 => versions.Where(version => version.IsPreview),
            _ => versions
        };
        if (!string.IsNullOrWhiteSpace(SearchText))
            versions = versions.Where(version => version.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        Versions.Clear();
        foreach (var version in versions) Versions.Add(version);
        if (!IsLoading && !IsInstalling)
            StatusText = $"共 {Versions.Count} 个版本";
    }

    public string? GetInstallDetails(BedrockGdkVersion version)
    {
        if (GetInstallFolder() is not { } folder) return null;
        var destination = Path.Combine(folder.FolderPath, "bedrock_versions", version.Id);
        return $"版本：{version.Id}\n渠道：{version.ChannelLabel}\n构建：GDK x64\n发布日期：{version.ReleaseTime:g}\n\n安装目录：{destination}\n\n确认后将联网下载、校验并安装该版本。";
    }

    private MinecraftFolderEntry? GetInstallFolder() => Data.ConfigEntry.DefaultMinecraftFolder is
        { DetectedLayout.Kind: MinecraftFolderKind.Standard } folder ? folder :
        Data.ConfigEntry.MinecraftFolders.FirstOrDefault(item => item.DetectedLayout.Kind == MinecraftFolderKind.Standard);
}
