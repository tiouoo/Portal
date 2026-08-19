using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Module.Initialize;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common.Helpers;
using TioUi.Controls;

namespace Portal.Views;

public partial class BedrockPackageImportWindow : TioWindow
{
    private readonly IntPtr _macOsWindowHandle;

    public BedrockPackageImportWindow() : this(string.Empty)
    {
    }

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
        viewModel.StatusText = CommonLanguageManager.Instance.bedrockPackageImportWindow_reading.CurrentValue();
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
            viewModel.StatusText = string.Format(
                CommonLanguageManager.Instance.bedrockPackageImportWindow_initFailed.CurrentValue(),
                exception.Message);
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
            MacOsWindowHandler.RefreshTitleBarButtonPosition(_macOsWindowHandle);
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

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

public partial class BedrockPackageImportWindowViewModel : ObservableObject
{
    private readonly string _archivePath;
    private readonly BedrockPackageInspection? _inspection;
    private Bitmap? _packageIcon;

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
                new BedrockPackageInspection(BedrockPackageArchiveType.Mcpack,
                    Path.GetFileNameWithoutExtension(archivePath), []));
            StatusText = string.Format(
                CommonLanguageManager.Instance.bedrockPackageImportWindow_cannotRead.CurrentValue(),
                exception.Message);
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

    public BedrockPackageImportDialogViewModel ViewModel { get; }
    public string ImportTitle => GetImportTitle(_inspection);
    public string WindowTitle =>
        string.Format(CommonLanguageManager.Instance.bedrockPackageImportWindow_windowTitle.CurrentValue(),
            ImportTitle);

    public string PackageDescription =>
        string.IsNullOrWhiteSpace(PrimaryContent?.Description)
            ? CommonLanguageManager.Instance.bedrockServers_noDescription.CurrentValue()
            : PrimaryContent!.Description.Trim();

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public string ImportButtonText => IsBusy
        ? CommonLanguageManager.Instance.bedrockPackageImportWindow_importing.CurrentValue()
        : CommonLanguageManager.Instance.bedrockPackageImportWindow_import.CurrentValue();
    public bool CanImport => !IsBusy && _inspection != null && ViewModel.CanImport;
    public bool IsInitialized { get; }

    public BedrockPackageContent? PrimaryContent =>
        _inspection?.Contents.FirstOrDefault();

    public string PackageName => PrimaryContent?.Name ?? _inspection?.DisplayName ??
                                 CommonLanguageManager.Instance.bedrockPackageImportWindow_noInfo.CurrentValue();

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
                _ => hasEngine ? engine : CommonLanguageManager.Instance.bedrockPackageImportWindow_noVersion.CurrentValue()
            };
        }
    }

    public string PackageAuthorsText =>
        PrimaryContent?.Authors is { Count: > 0 } authors
            ? $"{string.Join("、", authors)}"
            : CommonLanguageManager.Instance.bedrockPackageImportWindow_noAuthors.CurrentValue();

    public Bitmap? PackageIcon => _packageIcon ??= CreateIcon(PrimaryContent?.IconData);
    public bool HasPackageIcon => PackageIcon != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportButtonText))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool IsBusy { get; set; }

    private static Bitmap? CreateIcon(byte[]? data)
    {
        if (data == null) return null;
        try
        {
            return new Bitmap(new MemoryStream(data));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GetImportTitle(BedrockPackageInspection? inspection)
    {
        if (inspection == null)
            return CommonLanguageManager.Instance.bedrockPackageImport_title.CurrentValue();
        if (inspection.ArchiveType == BedrockPackageArchiveType.Mcworld)
            return CommonLanguageManager.Instance.bedrockPackageImportWindow_importWorld.CurrentValue();

        var contentTypes = inspection.Contents.Select(content => content.Type).Distinct().ToArray();
        if (contentTypes.Length != 1)
            return CommonLanguageManager.Instance.bedrockPackageImportWindow_importAddon.CurrentValue();
        return contentTypes[0] switch
        {
            BedrockPackageContentType.ResourcePack =>
                CommonLanguageManager.Instance.bedrockPackageImportWindow_importResourcePack.CurrentValue(),
            BedrockPackageContentType.BehaviorPack =>
                CommonLanguageManager.Instance.bedrockPackageImportWindow_importBehaviorPack.CurrentValue(),
            BedrockPackageContentType.SkinPack =>
                CommonLanguageManager.Instance.bedrockPackageImportWindow_importSkinPack.CurrentValue(),
            BedrockPackageContentType.WorldTemplate =>
                CommonLanguageManager.Instance.bedrockPackageImportWindow_importWorldTemplate.CurrentValue(),
            _ => CommonLanguageManager.Instance.bedrockPackageImport_title.CurrentValue()
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
        StatusText = CommonLanguageManager.Instance.bedrockPackageImportWindow_importingWait.CurrentValue();
        try
        {
            await Task.Run(() => new BedrockPackageImportService().Import(_archivePath, _inspection,
                ViewModel.SelectedInstance.Instance, ViewModel.SelectedWorldUserId));
            StatusText = CommonLanguageManager.Instance.bedrockPackageImportWindow_importComplete.CurrentValue();
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            StatusText = string.Format(
                CommonLanguageManager.Instance.bedrockPackageImportWindow_importFailed.CurrentValue(),
                exception.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}