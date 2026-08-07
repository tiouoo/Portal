using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Services;
using Portal.Module.Initialize;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Controls;

namespace Portal.Views;

public partial class BedrockPackageImportWindow : TioWindow
{
    private IntPtr _macOsWindowHandle;

    public BedrockPackageImportWindow() : this(string.Empty) { }

    public BedrockPackageImportWindow(string archivePath)
    {
        InitializeComponent();
        DataContext = new BedrockPackageImportWindowViewModel(archivePath, false);
        Loaded += async (_, _) => await InitializeAsync(archivePath);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        _macOsWindowHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_macOsWindowHandle == IntPtr.Zero)
            return;
        Loaded += (_, _) => RefreshMacOsTitleBarButtons();
        PropertyChanged += Window_OnPropertyChanged;
        SizeChanged += (_, _) => RefreshMacOsTitleBarButtons();
    }

    private async Task InitializeAsync(string archivePath)
    {
        if (DataContext is not BedrockPackageImportWindowViewModel viewModel || viewModel.IsInitialized)
            return;

        viewModel.IsBusy = true;
        viewModel.StatusText = "正在读取包和基岩版实例...";
        try
        {
            var inspectionTask = Task.Run(() => new BedrockPackageImportService().Inspect(archivePath));
            var instancesTask = Initializer.LoadBedrockPackageImportDataAsync();
            await Task.WhenAll(inspectionTask, instancesTask);
            DataContext = new BedrockPackageImportWindowViewModel(archivePath, inspectionTask.Result);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            viewModel.StatusText = $"初始化失败：{exception.Message}";
            viewModel.IsBusy = false;
        }
    }

    private void Window_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(WindowState))
            RefreshMacOsTitleBarButtons();
    }

    private void RefreshMacOsTitleBarButtons()
    {
        try
        {
            TioUi.Common.Helpers.MacOsWindowHandler.RefreshTitleBarButtonPosition(_macOsWindowHandle, x: 14, y: 2,
                spacing: 20);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BedrockPackageImportWindowViewModel viewModel)
            return;
        if (await viewModel.ImportAsync())
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close();
}

public partial class BedrockPackageImportWindowViewModel : ObservableObject
{
    private readonly string _archivePath;
    private readonly BedrockPackageInspection? _inspection;

    public BedrockPackageImportDialogViewModel ViewModel { get; }
    public string ImportTitle => GetImportTitle(_inspection);
    public string WindowTitle => $"Portal - {ImportTitle}";
    public string PackageDescription =>
        string.IsNullOrWhiteSpace(PrimaryContent?.Description) ? "暂无描述" : PrimaryContent!.Description.Trim();
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public string ImportButtonText => IsBusy ? "导入中" : "导入";
    public bool CanImport => !IsBusy && _inspection != null && ViewModel.CanImport;
    public bool IsInitialized { get; }

    public BedrockPackageContent? PrimaryContent =>
        _inspection?.Contents.FirstOrDefault();
    public string PackageName => PrimaryContent?.Name ?? (_inspection?.DisplayName ?? "暂无信息");
    public string PackageVersionText
    {
        get
        {
            var version = PrimaryContent?.Version;
            var engine = PrimaryContent?.MinEngineVersion;

            var hasVersion = !string.IsNullOrWhiteSpace(version);
            var hasEngine = !string.IsNullOrWhiteSpace(engine);

            return hasVersion switch
            {
                true when hasEngine => $"{version} ({engine})",
                true => version,
                _ => hasEngine ? engine : "暂无版本信息"
            };
        }
    }
    public string PackageAuthorsText =>
        PrimaryContent?.Authors is { Count: > 0 } authors ? $"{string.Join("、", authors)}" : "暂无作者信息";
    public Bitmap? PackageIcon => _packageIcon ??= CreateIcon(PrimaryContent?.IconData);
    public bool HasPackageIcon => PackageIcon != null;
    private Bitmap? _packageIcon;

    private static Bitmap? CreateIcon(byte[]? data)
    {
        if (data == null) return null;
        try { return new Bitmap(new MemoryStream(data)); }
        catch (Exception) { return null; }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportButtonText))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool IsBusy { get; set; }

    public BedrockPackageImportWindowViewModel(string archivePath, bool initialize = true)
    {
        _archivePath = archivePath;
        IsInitialized = initialize;
        if (!initialize)
        {
            ViewModel = new BedrockPackageImportDialogViewModel(
                new BedrockPackageInspection(BedrockPackageArchiveType.Mcpack,
                    Path.GetFileNameWithoutExtension(archivePath), []));
            return;
        }

        try
        {
            _inspection = new BedrockPackageImportService().Inspect(archivePath);
            ViewModel = new BedrockPackageImportDialogViewModel(_inspection);
            ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        }
        catch (Exception exception)
        {
            ViewModel = new BedrockPackageImportDialogViewModel(
                new BedrockPackageInspection(BedrockPackageArchiveType.Mcpack, Path.GetFileNameWithoutExtension(archivePath), []));
            StatusText = $"无法读取此文件：{exception.Message}";
        }
    }

    public BedrockPackageImportWindowViewModel(string archivePath, BedrockPackageInspection inspection)
    {
        _archivePath = archivePath;
        _inspection = inspection;
        IsInitialized = true;
        ViewModel = new BedrockPackageImportDialogViewModel(inspection);
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    private static string GetImportTitle(BedrockPackageInspection? inspection)
    {
        if (inspection == null)
            return "导入基岩版包";
        if (inspection.ArchiveType == BedrockPackageArchiveType.Mcworld)
            return "导入世界存档";

        var contentTypes = inspection.Contents.Select(content => content.Type).Distinct().ToArray();
        if (contentTypes.Length != 1)
            return "导入附加包";
        return contentTypes[0] switch
        {
            BedrockPackageContentType.ResourcePack => "导入资源包",
            BedrockPackageContentType.BehaviorPack => "导入行为包",
            BedrockPackageContentType.SkinPack => "导入皮肤包",
            BedrockPackageContentType.WorldTemplate => "导入世界模板",
            _ => "导入基岩版包"
        };
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BedrockPackageImportDialogViewModel.CanImport))
            OnPropertyChanged(nameof(CanImport));
    }

    public async Task<bool> ImportAsync()
    {
        if (!CanImport || _inspection == null || ViewModel.SelectedInstance == null)
            return false;

        IsBusy = true;
        StatusText = "正在导入，请稍候...";
        try
        {
            await Task.Run(() => new BedrockPackageImportService().Import(_archivePath, _inspection,
                ViewModel.SelectedInstance.Instance, ViewModel.SelectedWorldUserId));
            StatusText = "导入完成";
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            StatusText = $"导入失败：{exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
