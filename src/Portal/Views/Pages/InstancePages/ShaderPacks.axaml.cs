using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class ShaderPacks : UserControl, INotifyPropertyChanged
{
    private readonly MinecraftInstance? _instance;
    private bool _hasLoaded;
    private bool _isLoading;
    private string _filter = string.Empty;

    public ObservableCollection<ShaderPackItem> Items { get; } = [];
    public ObservableCollection<ShaderPackItem> FilteredItems { get; } = [];
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand ClearSelectionCommand { get; }
    public IRelayCommand InvertSelectionCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            RaisePropertyChanged(nameof(IsLoading));
        }
    }

    public bool IsEmpty => !IsLoading && FilteredItems.Count == 0;
    public string ShaderPackCountText => IsLoading ? string.Empty : $"{FilteredItems.Count} 个";
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText => $"批量操作{SelectedCount}个";
    public bool HasMultipleSelection => SelectedCount >= 1;

    public ShaderPacks()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Resource_OnDragOver);
        AddHandler(DragDrop.DropEvent, Resource_OnDrop);
        SelectAllCommand = new RelayCommand(() => SetSelection(item => true));
        ClearSelectionCommand = new RelayCommand(() => SetSelection(item => false));
        InvertSelectionCommand = new RelayCommand(() => SetSelection(item => !item.IsSelected));
        DataContext = this;
        // 文本框聚焦时不拦截 Ctrl+A，保留全选文本的默认行为
        KeyBindings.Add(new KeyBinding
        {
            Command = new RelayCommand(() => SetSelection(item => true), () => !IsTextInputFocused()),
            Gesture = KeyGesture.Parse("ctrl+A")
        });
        KeyBindings.Add(new KeyBinding { Command = ClearSelectionCommand, Gesture = KeyGesture.Parse("ctrl+Shift+A") });
        KeyBindings.Add(new KeyBinding { Command = InvertSelectionCommand, Gesture = KeyGesture.Parse("ctrl+I") });
    }

    public ShaderPacks(MinecraftInstance instance) : this() => _instance = instance;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null) return;

        _hasLoaded = true;
        IsLoading = true;
        RaiseListProperties();
        var folder = _instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder);
        var files = await Task.Run(() => Directory.Exists(folder)
             ? Directory.EnumerateFiles(folder).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
             : []);
        Items.Clear();
        RaiseSelectionProperties();
        foreach (var file in files)
            Items.Add(new ShaderPackItem(file));
        ApplyFilter();
        IsLoading = false;
        RaiseListProperties();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_filter)
            ? Items
            : Items.Where(item => item.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in filtered)
            FilteredItems.Add(item);
        RaiseListProperties();
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null) return;
        await topLevel.Launcher.LaunchDirectoryInfoAsync(
            new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder)));
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e) => await ImportAsync(null);
    private void Resource_OnDragOver(object? sender, DragEventArgs e)
    {
        if (JavaResourceImport.Accepts(e.DataTransfer, ".zip")) { e.DragEffects = DragDropEffects.Copy; e.Handled = true; }
    }
    private async void Resource_OnDrop(object? sender, DragEventArgs e) => await ImportAsync(e);
    private async Task ImportAsync(DragEventArgs? drop)
    {
        if (_instance == null) return;
        var refresh = async () => { _hasLoaded = false; await LoadAsync(); };
        var destination = _instance.GetSpecialFolder(MinecraftSpecialFolder.ShaderPacksFolder);
        if (drop == null) await JavaResourceImport.SelectAndImportAsync(this, "选择光影包", destination, "光影包", [".zip"], false, refresh);
        else await JavaResourceImport.ImportDropAsync(this, drop, destination, "光影包", [".zip"], false, refresh);
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _hasLoaded = false;
        _ = LoadAsync();
    }

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _filter = (sender as TextBox)?.Text ?? string.Empty;
        ApplyFilter();
    }

    private void ShaderPackCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            (sender as Control)?.DataContext is not ShaderPackItem item)
            return;

        item.IsSelected = !item.IsSelected;
        RaiseSelectionProperties();
    }

    private bool IsTextInputFocused() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or Avalonia.Controls.AutoCompleteBox or TioUi.Controls.AutoCompleteBox;

    private void SelectAll_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => true);
    private void ClearSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => false);
    private void InvertSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => !item.IsSelected);
    private async void DeleteSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedItems();
        if (selected.Length < 1) return;

        var result = await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Thickness(24), Text = $"确定要永久删除选中的 {selected.Length} 个光影包吗？此操作无法撤销。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        }, null, this.TryGetHostId(), CreateDeleteConfirmationOptions());
        if (result == DialogResult.Yes)
            await RunSelectedFileActionAsync(selected, item => File.Delete(item.FilePath), "删除");
    }

    private async void ShowShaderPackDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetShaderPackItem(sender) is not { } item) return;

        await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Thickness(24), Text = $"文件名：{item.FileName}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        }, null, this.TryGetHostId(),
            new OverlayDialogOptions { Title = "光影包详情", Buttons = DialogButton.OK, CanResize = false });
    }

    private async void DeleteShaderPack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetShaderPackItem(sender) is not { } item) return;

        var result = await OverlayDialog.ShowStandardAsync(new TextBlock
        {
            Margin = new Thickness(24), Text = $"确定要永久删除光影包“{item.FileName}”吗？此操作无法撤销。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        }, null, this.TryGetHostId(), CreateDeleteConfirmationOptions());
        if (result == DialogResult.Yes)
            await RunSelectedFileActionAsync([item], shaderPack => File.Delete(shaderPack.FilePath), "删除");
    }

    private async void OpenShaderPackFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetShaderPackItem(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"") { UseShellExecute = true });
            return;
        }

        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(item.FilePath)!));
    }

    private async Task RunSelectedFileActionAsync(IEnumerable<ShaderPackItem> selected, Action<ShaderPackItem> action, string actionName)
    {
        var failed = 0;
        foreach (var item in selected)
        {
            try { action(item); }
            catch (IOException) { failed++; }
            catch (UnauthorizedAccessException) { failed++; }
        }

        _hasLoaded = false;
        await LoadAsync();
        ShowNotice(failed == 0 ? $"已{actionName}所选光影包" : $"{actionName}完成，但有 {failed} 个光影包操作失败",
            failed == 0 ? NotificationType.Success : NotificationType.Warning);
    }

    private ShaderPackItem[] GetSelectedItems() => Items.Where(item => item.IsSelected).ToArray();
    private static ShaderPackItem? GetShaderPackItem(object? sender) => (sender as Control)?.Tag as ShaderPackItem;
    private static OverlayDialogOptions CreateDeleteConfirmationOptions() => new()
    {
        Title = "删除光影包", Mode = DialogMode.Error, Buttons = DialogButton.YesNo,
        OverrideYesButtonText = "删除", OverrideNoButtonText = "取消", CanLightDismiss = false, CanResize = false
    };

    private void SetSelection(Func<ShaderPackItem, bool> selection)
    {
        foreach (var item in Items)
            item.IsSelected = selection(item);
        RaiseSelectionProperties();
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(ShaderPackCountText));
    }

    private void RaiseSelectionProperties()
    {
        RaisePropertyChanged(nameof(SelectedCount));
        RaisePropertyChanged(nameof(SelectedCountText));
        RaisePropertyChanged(nameof(HasMultipleSelection));
    }

    private void ShowNotice(string message, NotificationType type)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            NotificationGateway.Notice(topLevel, message, type);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ShaderPackItem(string filePath) : INotifyPropertyChanged
{
    private bool _isSelected;
    public string FilePath { get; } = filePath;
    public string FileName { get; } = Path.GetFileName(filePath);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
