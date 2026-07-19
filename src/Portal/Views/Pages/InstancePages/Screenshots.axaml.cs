using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Portal.Views.StaticPages;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class Screenshots : UserControl, INotifyPropertyChanged
{
    public ObservableCollection<ScreenshotItem> ScreenshotItems { get; } = [];

    public ReadOnlyObservableCollection<ScreenshotItem> ScreenshotList { get; }

    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
                return;

            _isLoading = value;
            RaisePropertyChanged(nameof(IsLoading));
        }
    }

    public bool IsEmpty => !IsLoading && ScreenshotItems.Count == 0;
    public string ScreenshotCountText => IsLoading ? string.Empty : $"{ScreenshotItems.Count} 张";

    private MinecraftInstance? _instance;
    private bool _hasLoaded;

    public Screenshots()
    {
        ScreenshotList = new ReadOnlyObservableCollection<ScreenshotItem>(ScreenshotItems);
        InitializeComponent();
        DataContext = this;
    }

    public Screenshots(MinecraftInstance instance) : this()
    {
        _instance = instance;
        instance.PropertyChanged += Instance_PropertyChanged;
        _ = instance.StorageUsage.EnsureLoadedAsync();
        AttachedToVisualTree += async (_, _) => await LoadAsync();
        DetachedFromVisualTree += (_, _) => instance.PropertyChanged -= Instance_PropertyChanged;
    }

    private string? ScreenshotsPath => _instance?.GetSpecialFolder(MinecraftSpecialFolder.ScreenshotsFolder);

    private void Instance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MinecraftInstance.EnableIndependentBedrockVersion))
        {
            _hasLoaded = false;
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var screenshotsPath = ScreenshotsPath;
        if (_hasLoaded || string.IsNullOrEmpty(screenshotsPath))
            return;
        
        ScreenshotItems.Clear();

        _hasLoaded = true;
        IsLoading = true;
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(ScreenshotCountText));

        var files = await Task.Run(() =>
        {
            if (!Directory.Exists(screenshotsPath))
                return [];

            return Directory.EnumerateFiles(screenshotsPath)
                .Where(path => ScreenshotItem.IsSupported(path))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => new ScreenshotItem(file.FullName, file.Name))
                .ToArray();
        });

        foreach (var item in files)
            ScreenshotItems.Add(item);

        IsLoading = false;
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(ScreenshotCountText));
    }

    private static ScreenshotItem? GetItem(object? sender) => (sender as Control)?.Tag as ScreenshotItem;

    private void OpenImageViewer_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Control { DataContext: ScreenshotItem item } ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        Portal.Views.StaticPages.ImageViewer.Open(item.FilePath, topLevel);
        e.Handled = true;
    }

    private async void CopyScreenshot_OnClick(object? sender, RoutedEventArgs e)
    {
        var item = GetItem(sender);
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (item == null || clipboard == null || !File.Exists(item.FilePath))
            return;

        await using var stream = File.OpenRead(item.FilePath);
        await clipboard.SetBitmapAsync(new Bitmap(stream));
    }

    private async void OpenScreenshot_OnClick(object? sender, RoutedEventArgs e)
    {
        var item = GetItem(sender);
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (item == null || storage == null || !File.Exists(item.FilePath))
            return;

        var file = await storage.TryGetFileFromPathAsync(new Uri(item.FilePath));
        if (file != null)
            await TopLevel.GetTopLevel(this).Launcher.LaunchFileAsync(file);
    }

    private async void SaveScreenshotAs_OnClick(object? sender, RoutedEventArgs e)
    {
        var item = GetItem(sender);
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (item == null || storage == null || !File.Exists(item.FilePath))
            return;

        var destination = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = item.FileName
        });
        if (destination == null)
            return;

        await using var source = File.OpenRead(item.FilePath);
        await using var target = await destination.OpenWriteAsync();
        await source.CopyToAsync(target);
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var screenshotsPath = ScreenshotsPath;
        if (!string.IsNullOrEmpty(screenshotsPath))
            await TopLevel.GetTopLevel(this).Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(screenshotsPath));
    }

    private async void DeleteScreenshot_OnClick(object? sender, RoutedEventArgs e)
    {
        var item = GetItem(sender);
        if (item == null || !File.Exists(item.FilePath))
            return;

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock { Margin = new Avalonia.Thickness(24), Text = $"确定要永久删除截图“{item.FileName}”吗？此操作无法撤销。", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "删除截图",
                Mode = DialogMode.Error,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "删除",
                OverrideNoButtonText = "取消",
                CanLightDismiss = false,
                CanResize = false
            });
        if (result != DialogResult.Yes)
            return;

        try
        {
            File.Delete(item.FilePath);
            ScreenshotItems.Remove(item);
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(ScreenshotCountText));
        }
        catch (IOException)
        {
            // A file held by another process should remain visible rather than desynchronizing the gallery.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _hasLoaded = false;
        _ = LoadAsync();
    }
}

public sealed class ScreenshotItem(string filePath, string fileName)
{
    public string FilePath { get; } = filePath;
    public string FileName { get; } = fileName;

    // The loader runs off the UI thread and decodes only enough pixels for the gallery tile.
    public IAsyncImageLoader ImageLoader { get; } = new ScreenshotImageLoader(480);

    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
}

public sealed class ScreenshotImageLoader(int decodeWidth) : IAsyncImageLoader
{
    public Task<Bitmap?> ProvideImageAsync(string url) => Task.Run<Bitmap?>(() =>
    {
        try
        {
            using var stream = File.OpenRead(url);
            return Bitmap.DecodeToWidth(stream, decodeWidth);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    });

    public void Dispose()
    {
    }
}
