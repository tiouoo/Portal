using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockWorlds : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly MinecraftInstance? _instance;
    private readonly BedrockWorldService _worldService = new();
    private string _filter = string.Empty;
    private bool _isLoading, _isDisposed;
    private int _loadSequence;

    public BedrockWorlds()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Resource_OnDragOver);
        AddHandler(DragDrop.DropEvent, Resource_OnDrop);
        DataContext = this;
    }

    public BedrockWorlds(MinecraftInstance instance) : this()
    {
        _instance = instance;
    }

    public ObservableCollection<string> WorldUserIds { get; } = [];
    public ObservableCollection<BedrockWorldItem> Items { get; } = [];
    public ObservableCollection<BedrockWorldItem> FilteredItems { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                RaisePropertyChanged(nameof(IsLoading));
            }
        }
    }

    public bool IsEmpty => !IsLoading && FilteredItems.Count == 0;
    public string CountText => IsLoading ? string.Empty : $"{FilteredItems.Count} 个";

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _disposeCancellation.Cancel();
        foreach (var item in Items) item.Dispose();
        _disposeCancellation.Dispose();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshWorldUserIds();
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel && !string.IsNullOrEmpty(GetSelectedWorldsFolder()))
            await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(GetSelectedWorldsFolder()));
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        await ImportAsync(null);
    }

    private void Resource_OnDragOver(object? sender, DragEventArgs e)
    {
        if (BedrockResourceImport.Accepts(e.DataTransfer, ".mcworld"))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void Resource_OnDrop(object? sender, DragEventArgs e)
    {
        await ImportAsync(e);
    }

    private async Task ImportAsync(DragEventArgs? drop)
    {
        if (_instance == null) return;
        var userId = WorldUserIdSelector.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(userId)) return;
        if (drop == null)
            await BedrockResourceImport.SelectAndImportAsync(this, _instance, "选择基岩版存档", "存档", [".mcworld"], userId,
                null, LoadAsync);
        else
            await BedrockResourceImport.ImportDropAsync(this, drop, _instance, "存档", [".mcworld"], userId, null,
                LoadAsync);
    }

    private async void OpenWorldFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not BedrockWorldItem item || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Info.FolderPath}\"")
                { UseShellExecute = true });
            return;
        }

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(item.Info.FolderPath));
    }

    private async void DeleteWorld_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not BedrockWorldItem item || !Directory.Exists(item.Info.FolderPath))
            return;

        var result = await OverlayDialog.ShowStandardAsync(
            new TextBlock
            {
                Margin = new Thickness(24),
                Text = $"确定要永久删除存档“{item.DisplayName}”吗？此操作无法撤销。",
                TextWrapping = TextWrapping.Wrap
            },
            null, this.TryGetHostId(), new OverlayDialogOptions
            {
                Title = "删除存档",
                Mode = DialogMode.Error,
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "删除",
                OverrideNoButtonText = "取消",
                CanLightDismiss = false,
                CanResize = false
            });
        if (result != DialogResult.Yes) return;

        try
        {
            Logger.Info($"[BedrockWorlds] Deleting world {item.DisplayName} at {item.Info.FolderPath}.");
            Directory.Delete(item.Info.FolderPath, true);
            Items.Remove(item);
            ApplyFilter();
            if (TopLevel.GetTopLevel(this) is { } topLevel)
                topLevel.Notice("存档已删除", NotificationType.Success);
        }
        catch (IOException ex)
        {
            Logger.Warning($"[BedrockWorlds] Failed to delete world {item.Info.FolderPath}: {ex}");
            ShowDeleteFailure(ex.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Logger.Warning($"[BedrockWorlds] Failed to delete world {item.Info.FolderPath}: {exception}");
            ShowDeleteFailure("没有删除此存档的权限。");
        }
    }

    private void ShowDeleteFailure(string message)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.Notice($"无法删除存档：{message}", NotificationType.Error);
    }

    private void RefreshWorldUserIds()
    {
        if (_instance?.BedrockConfig is not { } config) return;
        var selectedUserId = WorldUserIdSelector.SelectedItem as string;
        var userIds = BedrockDataPathResolver.GetWorldUserIds(config);
        WorldUserIds.Clear();
        foreach (var userId in userIds) WorldUserIds.Add(userId);
        WorldUserIdSelector.SelectedItem = selectedUserId != null && WorldUserIds.Contains(selectedUserId)
            ? selectedUserId
            : WorldUserIds.FirstOrDefault(userId =>
                  !string.Equals(userId, "Shared", StringComparison.OrdinalIgnoreCase))
              ?? WorldUserIds.FirstOrDefault();
    }

    private string GetSelectedWorldsFolder()
    {
        if (_instance?.BedrockConfig is not { } config) return string.Empty;
        var path = BedrockDataPathResolver.GetWorldsFolder(config,
            WorldUserIdSelector.SelectedItem as string ?? "Shared");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    private void WorldUserIdSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_instance?.BedrockConfig is not { } config || _isDisposed) return;
        var sequence = ++_loadSequence;
        IsLoading = true;
        RaiseListProperties();
        try
        {
            var worlds = await _worldService.ScanAsync(config, WorldUserIdSelector.SelectedItem as string ?? "Shared",
                _disposeCancellation.Token);
            if (_isDisposed || sequence != _loadSequence) return;
            foreach (var item in Items) item.Dispose();
            Items.Clear();
            foreach (var world in worlds) Items.Add(new BedrockWorldItem(world));
            ApplyFilter();
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[BedrockWorlds] World scan cancelled: {exception}");
        }
        finally
        {
            if (!_isDisposed && sequence == _loadSequence)
            {
                IsLoading = false;
                RaiseListProperties();
            }
        }
    }

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _filter = (sender as TextBox)?.Text ?? string.Empty;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_filter)
            ? Items
            : Items.Where(item =>
                item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                item.FolderName.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in filtered) FilteredItems.Add(item);
        RaiseListProperties();
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = LoadAsync();
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(CountText));
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class BedrockWorldItem(BedrockWorldInfo info) : IDisposable
{
    public BedrockWorldInfo Info { get; } = info;
    public string DisplayName => Info.DisplayName;
    public string FolderName => Info.FolderName;
    public string FolderNameText => $"文件夹：{FolderName}";
    public string PackSummary => $"资源包 {Info.ResourcePacks.Count} 个，行为包 {Info.BehaviorPacks.Count} 个";
    public string ModifiedText => $"修改时间：{Info.LastWriteTime:yyyy-MM-dd HH:mm}";
    public Bitmap? Icon { get; } = CreateIcon(info.IconData);
    public bool HasIcon => Icon != null;

    public void Dispose()
    {
        Icon?.Dispose();
    }

    private static Bitmap? CreateIcon(byte[]? data)
    {
        if (data == null) return null;
        try
        {
            return new Bitmap(new MemoryStream(data));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}