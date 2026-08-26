using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Irihi.Lingua;
using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Localization;
using Portal.ViewModels;
using Portal.Views.Components;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Extensions;
using TioUi.Common;
using TioUi.Controls;
using TioUi.Shared;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("pages_appearance", "pages_appearancePath", "Appearance")]
public partial class Appearance : Dsc, INotifyPropertyChanged
{
    private double _currentRenderScaling = 1.0;
    private DispatcherTimer? _monitorTimer;
    private TopLevel? _topLevel;

    public IList<ILinguaManager> Managers { get; } = LocalizationService.RegisteredManagers.ToList();

    public IList<LinguaCulture> Cultures { get; } =
    [
        new()
        {
            Culture = new CultureInfo("zh-CN"), DisplayName = CommonLanguageManager.Instance.common_languageChinese.CurrentValue(),
            ShortDisplayName = CommonLanguageManager.Instance.common_languageChineseShort.CurrentValue()
        },
        new() { Culture = new CultureInfo("en-US"), DisplayName = "English", ShortDisplayName = "EN" }
    ];

    public Appearance()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            ListBox.SelectedIndex = (int)Data.ConfigEntry.Theme;
            UpdateApplyButtonState();
            SubscribeRenderScaling();
        };
        Unloaded += (_, _) => { UnsubscribeRenderScaling(); };
        ListBox.SelectionChanged += (_, _) =>
        {
            if (ListBox.SelectedIndex == -1) return;
            Data.ConfigEntry.Theme = (Theme)ListBox.SelectedIndex;
        };
    }

    public double CurrentRenderScaling
    {
        get => _currentRenderScaling;
        private set
        {
            if (Math.Abs(_currentRenderScaling - value) < 0.0001) return;
            _currentRenderScaling = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveScale));
        }
    }

    public double EffectiveScale => CurrentRenderScaling * AppScaleSlider.Value;

    public object IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void SubscribeRenderScaling()
    {
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel == null)
        {
            AttachedToVisualTree += OnAttachedToVisualTree;
            return;
        }

        _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
        UpdateRenderScaling();

        _monitorTimer = new DispatcherTimer(TimeSpan.FromSeconds(0.5), DispatcherPriority.Background,
            (_, _) => { UpdateRenderScaling(); });
        _monitorTimer.Start();
    }

    private void UnsubscribeRenderScaling()
    {
        if (_topLevel != null)
        {
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
            _topLevel = null;
        }

        if (_monitorTimer != null)
        {
            _monitorTimer.Stop();
            _monitorTimer = null;
        }

        AttachedToVisualTree -= OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttachedToVisualTree;
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null)
        {
            _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
            UpdateRenderScaling();

            _monitorTimer = new DispatcherTimer(TimeSpan.FromSeconds(0.5), DispatcherPriority.Background,
                (_, _) => { UpdateRenderScaling(); });
            _monitorTimer.Start();
        }
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        UpdateRenderScaling();
    }

    private void UpdateRenderScaling()
    {
        if (_topLevel == null) return;
        CurrentRenderScaling = _topLevel.RenderScaling;
    }

    private void AppScaleSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateApplyButtonState();
        OnPropertyChanged(nameof(EffectiveScale));
    }

    private void ApplyScale_OnClick(object? sender, RoutedEventArgs e)
    {
        ApplyScale(AppScaleSlider.Value);
    }

    private async void CustomScale_OnClick(object? sender, RoutedEventArgs e)
    {
        var result = await OverlayDialog.ShowCustomAsync<ScaleInputDialog, ScaleInputDialogViewModel, double?>(
            new ScaleInputDialogViewModel(Data.ConfigEntry.AppScale), this.TryGetHostId());
        if (result is { } scale)
        {
            if (scale is < 0.5 or > 5) return;
            ApplyScale(scale);
        }
    }

    private void ApplyScale(double scale)
    {
        Data.ConfigEntry.AppScale = scale;
        UpdateApplyButtonState();
    }

    private async void ChangeBackgroundImage_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
            return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = SettingsLanguageManager.Instance.appearance_changeBackgroundImage.CurrentValue(),
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count == 0)
            return;

        var extension = Path.GetExtension(files[0].Name);
        var targetPath = Path.Combine(ConfigPath.BackgroundFolderPath, $"{Guid.NewGuid():N}{extension}");
        try
        {
            Directory.CreateDirectory(ConfigPath.BackgroundFolderPath);
            await using (var source = await files[0].OpenReadAsync())
            await using (var target = File.Create(targetPath))
                await source.CopyToAsync(target);

            var previousPath = Data.ConfigEntry.BackgroundImagePath;
            Data.ConfigEntry.BackgroundImagePath = targetPath;
            DeleteManagedBackground(previousPath, targetPath);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            if (File.Exists(targetPath))
                File.Delete(targetPath);
            TopLevel.GetTopLevel(this)?.Notice(
                string.Format(SettingsLanguageManager.Instance.appearance_changeBackgroundImageFailed.CurrentValue(),
                    exception.Message), NotificationType.Error);
        }
    }

    private static void DeleteManagedBackground(string? path, string currentPath)
    {
        if (string.IsNullOrEmpty(path) || string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
            return;

        var relativePath = Path.GetRelativePath(ConfigPath.BackgroundFolderPath, path);
        if (!relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            relativePath != ".." && File.Exists(path))
            File.Delete(path);
    }

    private void UpdateApplyButtonState()
    {
        var applied = Data.ConfigEntry.AppScale;
        var pending = AppScaleSlider.Value;
        ApplyScaleButton.IsEnabled = Math.Abs(pending - applied) > 0.0001;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void OnCultureChanged(object? sender, CultureChangedEventArgs e)
    {
        if (e.Culture?.CultureName is { } name)
            Data.ConfigEntry.Language = name;
    }
}
