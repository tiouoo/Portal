using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
            RefreshBackgroundHistory();
            SubscribeRenderScaling();
        };
        Unloaded += (_, _) =>
        {
            UnsubscribeRenderScaling();
            ClearBackgroundHistory();
        };
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

    public ObservableCollection<BackgroundHistoryItem> BackgroundHistory { get; } = [];

    public bool HasBackgroundHistory => BackgroundHistory.Count > 0;

    public object IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public bool IsWindowBorderCustomizationSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

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

            Data.ConfigEntry.BackgroundImagePath = targetPath;
            RefreshBackgroundHistory();
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            try
            {
                if (File.Exists(targetPath))
                    File.Delete(targetPath);
            }
            catch (Exception cleanupException)
            {
                Logger.Error(cleanupException);
            }
            TopLevel.GetTopLevel(this)?.Notice(
                string.Format(SettingsLanguageManager.Instance.appearance_changeBackgroundImageFailed.CurrentValue(),
                    exception.Message), NotificationType.Error);
        }
    }

    private void BackgroundHistory_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X + e.Delta.Y * -100, scrollViewer.Offset.Y);
        e.Handled = true;
    }

    private void BackgroundHistoryItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Border { Tag: BackgroundHistoryItem item })
            return;

        Data.ConfigEntry.BackgroundImagePath = item.Path;
        File.SetLastWriteTimeUtc(item.Path, DateTime.UtcNow);
        RefreshBackgroundHistory();
        e.Handled = true;
    }

    private void RemoveBackgroundHistoryItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: BackgroundHistoryItem item })
            return;

        try
        {
            if (string.Equals(Data.ConfigEntry.BackgroundImagePath, item.Path, StringComparison.OrdinalIgnoreCase))
                Data.ConfigEntry.BackgroundImagePath = null;
            File.Delete(item.Path);
            RefreshBackgroundHistory();
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            TopLevel.GetTopLevel(this)?.Notice(
                string.Format(SettingsLanguageManager.Instance.appearance_removeBackgroundFromHistoryFailed.CurrentValue(),
                    exception.Message), NotificationType.Error);
        }
    }

    private void RefreshBackgroundHistory()
    {
        ClearBackgroundHistory();
        if (Directory.Exists(ConfigPath.BackgroundFolderPath))
            foreach (var path in Directory.EnumerateFiles(ConfigPath.BackgroundFolderPath)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
                try
                {
                    using var stream = File.OpenRead(path);
                    var preview = Bitmap.DecodeToWidth(stream, 292);
                    var isSelected = string.Equals(path, Data.ConfigEntry.BackgroundImagePath,
                        StringComparison.OrdinalIgnoreCase);
                    BackgroundHistory.Add(new BackgroundHistoryItem(path, preview,
                        isSelected ? Brushes.DodgerBlue : Brushes.Transparent));
                }
                catch (Exception exception)
                {
                    Logger.Error(exception);
                }

        OnPropertyChanged(nameof(HasBackgroundHistory));
    }

    private void ClearBackgroundHistory()
    {
        foreach (var item in BackgroundHistory)
            item.Preview.Dispose();
        BackgroundHistory.Clear();
        OnPropertyChanged(nameof(HasBackgroundHistory));
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

public sealed record BackgroundHistoryItem(string Path, Bitmap Preview, IBrush BorderBrush);
